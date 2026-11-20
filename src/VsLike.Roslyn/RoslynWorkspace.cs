using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;
using VsLike.Core.Files;
using ClassifiedSpan = VsLike.Core.Code.ClassifiedSpan;

namespace VsLike.Roslyn;

/// <summary>
/// <see cref="ICodeWorkspace"/> auf Basis von Roslyn als Bibliothek. Die Solution wird als unveränderlicher
/// Snapshot gehalten; Editor-Text fließt per <c>WithDocumentText</c> ein und wird nie über <c>TryApplyChanges</c>
/// auf die Platte geschrieben.
/// </summary>
public sealed class RoslynWorkspace : ICodeWorkspace
{
    private static readonly TimeSpan DiagnosticsDelay = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly LooseFilesProject _looseFiles = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDiagnostics = new(StringComparer.Ordinal);
    private MSBuildWorkspace? _msbuild;
    private Solution? _solution;
    private IReadOnlyList<WorkspaceProject> _projects = [];
    private ImmutableArray<DiagnosticAnalyzer> _analyzers = [];

    public event EventHandler<DiagnosticsChangedEventArgs>? DiagnosticsChanged;

    public event EventHandler<WorkspaceLoadResult>? Loaded;

    public string? SolutionPath { get; private set; }

    public IReadOnlyList<WorkspaceProject> Projects => _projects;

    /// <summary>Gets the current solution snapshot including unsaved editor text.</summary>
    public Solution? CurrentSolution => _solution;

    /// <summary>
    /// Gets or sets additional analyzers (e.g. StyleCop) that run on top of the compiler. Setting it re-runs
    /// diagnostics for all loaded documents.
    /// </summary>
    public ImmutableArray<DiagnosticAnalyzer> Analyzers
    {
        get => _analyzers;
        set => _analyzers = value;
    }

    public async Task<WorkspaceLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        MSBuildBootstrap.EnsureRegistered();
        var stopwatch = Stopwatch.StartNew();
        var warnings = new ConcurrentQueue<string>();
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e => warnings.Enqueue(e.Diagnostic.Message));

        Solution solution;
        var fullPath = RealPath.Resolve(path);
        if (string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            solution = project.Solution;
        }
        else
        {
            solution = await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        MSBuildWorkspace? previous;
        lock (_gate)
        {
            previous = _msbuild;
            _msbuild = workspace;
            _solution = solution;
            SolutionPath = fullPath;
            _projects = [.. solution.Projects.Select(ToWorkspaceProject)];
        }

        previous?.Dispose();
        var result = new WorkspaceLoadResult(fullPath, _projects, [.. warnings], stopwatch.Elapsed);
        Loaded?.Invoke(this, result);
        return result;
    }

    public bool ContainsDocument(string filePath)
        => _solution?.GetDocumentIdsWithFilePath(filePath).IsEmpty == false;

    public void UpdateDocumentText(string filePath, string text)
    {
        var updated = false;
        lock (_gate)
        {
            if (_solution is { } solution)
            {
                var ids = solution.GetDocumentIdsWithFilePath(filePath);
                if (!ids.IsEmpty)
                {
                    var source = SourceText.From(text);
                    _solution = ids.Aggregate(solution, (s, id) => s.WithDocumentText(id, source, PreservationMode.PreserveIdentity));
                    updated = true;
                }
            }
        }

        if (!updated && IsCSharpFile(filePath))
        {
            _looseFiles.Upsert(filePath, text);
        }

        ScheduleDiagnostics(filePath);
    }

    public async Task<IReadOnlyList<DiagnosticInfo>> GetDocumentDiagnosticsAsync(string filePath, CancellationToken cancellationToken)
    {
        var document = GetDocument(filePath);
        if (document is null)
        {
            return [];
        }

        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return [];
        }

        var diagnostics = model.GetDiagnostics(cancellationToken: cancellationToken).ToList();
        var analyzers = _analyzers;
        if (!analyzers.IsEmpty && model.Compilation is { } compilation)
        {
            var withAnalyzers = compilation.WithAnalyzers(analyzers, document.Project.AnalyzerOptions);
            var tree = model.SyntaxTree;
            var syntax = await withAnalyzers.GetAnalyzerSyntaxDiagnosticsAsync(tree, cancellationToken).ConfigureAwait(false);
            var semantic = await withAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(model, null, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(syntax);
            diagnostics.AddRange(semantic);
        }

        return [.. diagnostics.Where(d => d.Severity != DiagnosticSeverity.Hidden && d.Location.IsInSource).Select(d => RoslynMapping.ToInfo(d, document.Project.Name))];
    }

    public async Task RefreshAllDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_solution is not { } solution)
        {
            return;
        }

        var all = new List<DiagnosticInfo>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents.Where(d => d.FilePath is not null && IsCSharpFile(d.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                all.AddRange(await GetDocumentDiagnosticsAsync(document.FilePath!, cancellationToken).ConfigureAwait(false));
            }
        }

        DiagnosticsChanged?.Invoke(this, new DiagnosticsChangedEventArgs(null, [.. all.DistinctBy(d => (d.Id, d.FilePath, d.Start, d.Message))]));
    }

    public async Task<IReadOnlyList<ClassifiedSpan>> ClassifyAsync(string filePath, int start, int length, CancellationToken cancellationToken)
    {
        var document = GetDocument(filePath);
        if (document is null)
        {
            return [];
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var span = TextSpan.FromBounds(Math.Clamp(start, 0, text.Length), Math.Clamp(start + length, 0, text.Length));
        var spans = await Classifier.GetClassifiedSpansAsync(document, span, cancellationToken).ConfigureAwait(false);
        return spans
            .Select(s => (Span: s.TextSpan, Name: RoslynMapping.MapClassification(s.ClassificationType)))
            .Where(s => s.Name is not null)
            .Select(s => new ClassifiedSpan(s.Span.Start, s.Span.Length, s.Name!))
            .ToList();
    }

    public async Task<QuickInfo?> GetQuickInfoAsync(string filePath, int offset, CancellationToken cancellationToken)
    {
        var document = GetDocument(filePath);
        if (document is null)
        {
            return null;
        }

        var diagnostics = (await GetDocumentDiagnosticsAsync(filePath, cancellationToken).ConfigureAwait(false))
            .Where(d => offset >= d.Start && offset <= d.Start + Math.Max(d.Length, 1))
            .ToList();
        var service = QuickInfoService.GetService(document);
        var item = service is null ? null : await service.GetQuickInfoAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return diagnostics.Count == 0 ? null : new QuickInfo(diagnostics[0].Start, diagnostics[0].Length, string.Empty, null, diagnostics);
        }

        var signature = string.Concat(item.Sections.Where(s => s.Kind == QuickInfoSectionKinds.Description).Select(s => s.Text));
        var docs = string.Join("\n", item.Sections.Where(s => s.Kind is not QuickInfoSectionKinds.Description).Select(s => s.Text).Where(t => t.Length > 0));
        return new QuickInfo(item.Span.Start, item.Span.Length, signature, docs.Length == 0 ? null : docs, diagnostics);
    }

    /// <summary>Re-runs diagnostics for every document (used after analyzer changes).</summary>
    public Task RecomputeAsync(CancellationToken cancellationToken) => RefreshAllDiagnosticsAsync(cancellationToken);

    /// <summary>Returns the document for a path from the loaded solution, or from the loose-files project.</summary>
    public Document? GetDocument(string filePath)
    {
        var solution = _solution;
        var id = solution?.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        return id is not null ? solution!.GetDocument(id) : _looseFiles.Find(filePath);
    }

    public void Dispose()
    {
        foreach (var pending in _pendingDiagnostics.Values)
        {
            pending.Cancel();
        }

        _msbuild?.Dispose();
        _looseFiles.Dispose();
    }

    private static bool IsCSharpFile(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceProject ToWorkspaceProject(Project project)
    {
        var references = project.ProjectReferences
            .Select(r => project.Solution.GetProject(r.ProjectId)?.FilePath)
            .OfType<string>()
            .ToList();
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var framework);
        project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
        return new WorkspaceProject(
            project.Name,
            project.FilePath ?? string.Empty,
            framework,
            string.IsNullOrEmpty(rootNamespace) ? project.DefaultNamespace : rootNamespace,
            references,
            [.. project.Documents.Select(d => d.FilePath).OfType<string>()]);
    }

    private void ScheduleDiagnostics(string filePath)
    {
        var cancellation = new CancellationTokenSource();
        var previous = _pendingDiagnostics.GetValueOrDefault(filePath);
        _pendingDiagnostics[filePath] = cancellation;
        previous?.Cancel();
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(DiagnosticsDelay, cancellation.Token).ConfigureAwait(false);
                    var diagnostics = await GetDocumentDiagnosticsAsync(filePath, cancellation.Token).ConfigureAwait(false);
                    if (!cancellation.IsCancellationRequested)
                    {
                        DiagnosticsChanged?.Invoke(this, new DiagnosticsChangedEventArgs(filePath, diagnostics));
                    }
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer edit.
                }
            },
            CancellationToken.None);
    }
}
