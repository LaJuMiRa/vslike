using Avalonia.Threading;
using VsLike.App.Editor;
using VsLike.App.Problems;
using VsLike.App.Shell;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Files;
using VsLike.Core.Localization;

namespace VsLike.App.Intelligence;

/// <summary>
/// Koordiniert Solution-Laden, Live-Diagnosen, Fehlerliste und die Verbindung der offenen C#-Dokumente zum Workspace.
/// </summary>
public sealed class CodeIntelligence : IDisposable
{
    private readonly ICodeWorkspace _workspace;
    private readonly ILanguageFeatures _features;
    private readonly IDotnetCli _cli;
    private readonly DocumentManager _documents;
    private readonly ProblemsViewModel _problems;
    private readonly ShellViewModel _shell;
    private readonly Dictionary<EditorDocumentViewModel, DocumentBinding> _bindings = [];
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    public CodeIntelligence(ICodeWorkspace workspace, ILanguageFeatures features, IDotnetCli cli, DocumentManager documents, ProblemsViewModel problems, ShellViewModel shell)
    {
        _workspace = workspace;
        _features = features;
        Applier = new WorkspaceEditApplier(documents, workspace);
        _cli = cli;
        _documents = documents;
        _problems = problems;
        _shell = shell;
        workspace.DiagnosticsChanged += (_, e) => Dispatcher.UIThread.Post(() => OnDiagnosticsChanged(e));
        documents.DocumentOpened += (_, document) => Bind(document);
        documents.DocumentClosed += (_, document) => Unbind(document);
        foreach (var document in documents.OpenDocuments)
        {
            Bind(document);
        }
    }

    public event EventHandler<WorkspaceLoadResult>? SolutionLoaded;

    /// <summary>Raised when the user ⌘-clicks a symbol in a C# editor (document, offset).</summary>
    public event EventHandler<(EditorDocumentViewModel Document, int Offset)>? DefinitionRequested;

    public ICodeWorkspace Workspace => _workspace;

    public ILanguageFeatures Features => _features;

    public WorkspaceEditApplier Applier { get; }

    public bool IsLoading { get; private set; }

    /// <summary>Restores (if needed) and loads a .sln/.slnx/.csproj, then analyses the whole solution in the background.</summary>
    public async Task<bool> OpenSolutionAsync(string path)
    {
        _loadCancellation?.Cancel();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        var fullPath = RealPath.Resolve(path);
        var name = Path.GetFileName(fullPath);
        IsLoading = true;
        try
        {
            if (NeedsRestore(fullPath))
            {
                _shell.StatusText = Loc.Format("Status.Restoring", name);
                var restore = await _cli.RunAsync(["restore", fullPath], Path.GetDirectoryName(fullPath), null, cancellation.Token).ConfigureAwait(true);
                if (!restore.Succeeded && !restore.WasCancelled)
                {
                    _shell.StatusText = Loc.Format("Status.LoadFailed", name, LastError(restore));
                }
            }

            _shell.StatusText = Loc.Format("Status.Loading", name);
            var result = await Task.Run(() => _workspace.LoadAsync(fullPath, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            _shell.StatusText = Loc.Format("Status.Loaded", name, result.Projects.Count, result.Duration.TotalSeconds);
            _problems.SetRoot(Path.GetDirectoryName(fullPath));
            foreach (var binding in _bindings.Values)
            {
                binding.ForceSync();
            }

            SolutionLoaded?.Invoke(this, result);
            _ = Task.Run(() => _workspace.RefreshAllDiagnosticsAsync(cancellation.Token), cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            _shell.StatusText = Loc.Format("Status.LoadFailed", name, ex.Message);
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loadCancellation?.Cancel();
        foreach (var binding in _bindings.Values)
        {
            binding.Dispose();
        }

        _bindings.Clear();

        _workspace.Dispose();
    }

    /// <summary>True if any project below the solution folder lacks a current obj/project.assets.json.</summary>
    internal static bool NeedsRestore(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var projects = solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? [solutionPath]
            : Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Where(p => !FileTreeBuilder.IsIgnored(directory, p));
        return projects.Any(project =>
        {
            var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
            return !File.Exists(assets) || File.GetLastWriteTimeUtc(assets) < File.GetLastWriteTimeUtc(project);
        });
    }

    /// <summary>Returns the binding of an open C# document, pushing its latest text to the workspace.</summary>
    internal DocumentBinding? BindingFor(EditorDocumentViewModel? document)
    {
        if (document is null || !_bindings.TryGetValue(document, out var binding))
        {
            return null;
        }

        binding.Sync();
        return binding;
    }

    private static string LastError(ProcessResult result)
        => result.Output.LastOrDefault(l => l.Text.Contains("error", StringComparison.OrdinalIgnoreCase))?.Text ?? $"exit code {result.ExitCode}";

    private static bool IsCSharp(EditorDocumentViewModel document) => document.Language.Id == "csharp";

    private void Bind(EditorDocumentViewModel document)
    {
        if (IsCSharp(document) && !_bindings.ContainsKey(document))
        {
            var binding = new DocumentBinding(_workspace, _features, Applier, document);
            binding.DefinitionRequested += (_, offset) => DefinitionRequested?.Invoke(this, (document, offset));
            _bindings[document] = binding;
        }
    }

    private void Unbind(EditorDocumentViewModel document)
    {
        if (_bindings.Remove(document, out var binding))
        {
            binding.Dispose();
        }
    }

    private void OnDiagnosticsChanged(DiagnosticsChangedEventArgs e)
    {
        // Background analysis may still report after the workbench was closed.
        if (_disposed)
        {
            return;
        }

        if (e.FilePath is null)
        {
            _problems.ReplaceSource(ProblemsViewModel.LiveSource, e.Diagnostics);
            var byFile = e.Diagnostics.Where(d => d.FilePath is not null).ToLookup(d => d.FilePath!, StringComparer.Ordinal);
            foreach (var binding in _bindings.Values.Where(b => b.FilePath is not null))
            {
                binding.ApplyDiagnostics([.. byFile[binding.FilePath!]]);
            }

            return;
        }

        if (_workspace.ContainsDocument(e.FilePath) || _bindings.Values.Any(b => b.FilePath == e.FilePath))
        {
            _problems.ReplaceFile(ProblemsViewModel.LiveSource, e.FilePath, e.Diagnostics);
        }

        foreach (var binding in _bindings.Values.Where(b => b.FilePath == e.FilePath))
        {
            binding.ApplyDiagnostics(e.Diagnostics);
        }
    }
}
