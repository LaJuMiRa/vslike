using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;
using VsLike.Core.Localization;

namespace VsLike.Roslyn.Features;

/// <summary>
/// Code-Fixes und Refactorings (⌘.). Die Provider werden einmalig per Reflection aus den Roslyn-Features-
/// Assemblies geladen (ohne MEF-Container); zusätzliche Fix-Provider (z. B. StyleCop) lassen sich anhängen.
/// </summary>
internal sealed class CodeActionFeature
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(3);
    private static readonly Lazy<(ImmutableArray<CodeFixProvider> Fixes, ImmutableArray<CodeRefactoringProvider> Refactorings)> Builtin = new(LoadBuiltin);

    public static int BuiltinFixProviderCount => Builtin.Value.Fixes.Length;

    public static int BuiltinRefactoringProviderCount => Builtin.Value.Refactorings.Length;

    public ImmutableArray<CodeFixProvider> AdditionalFixProviders { get; set; } = [];

    public static async Task<WorkspaceEdit> GetEditAsync(CodeActionInfo info, CancellationToken cancellationToken)
    {
        if (info.Handle is not Handle handle)
        {
            return WorkspaceEdit.Empty;
        }

        var operations = await handle.Action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var changed = operations.OfType<ApplyChangesOperation>().LastOrDefault()?.ChangedSolution;
        return changed is null ? WorkspaceEdit.Empty : await SolutionDiff.CreateAsync(handle.Solution, changed, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="diagnosticsOf">Diagnostics of any document (for "fix all" in document/project).</param>
    public async Task<IReadOnlyList<CodeActionInfo>> GetAsync(Document document, TextSpan span, ImmutableArray<Diagnostic> diagnostics, Func<Document, CancellationToken, Task<ImmutableArray<Diagnostic>>>? diagnosticsOf, CancellationToken cancellationToken)
    {
        var results = new List<CodeActionInfo>();
        var solution = document.Project.Solution;
        var fixProviders = Builtin.Value.Fixes.AddRange(AdditionalFixProviders);
        foreach (var diagnostic in diagnostics.Where(d => d.Location.SourceSpan.IntersectsWith(span) || d.Location.SourceSpan.Contains(span.Start)))
        {
            foreach (var provider in fixProviders.Where(p => p.FixableDiagnosticIds.Contains(diagnostic.Id)))
            {
                var registered = new List<CodeAction>();
                await RunSafelyAsync(() => provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => registered.Add(action), cancellationToken)), cancellationToken).ConfigureAwait(false);
                results.AddRange(Flatten(registered, isFix: true, solution, diagnostic.Id));
                if (diagnosticsOf is not null && registered.Count > 0)
                {
                    results.AddRange(await FixAllAsync(document, provider, registered[0], diagnostic, diagnostics, diagnosticsOf, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        foreach (var provider in Builtin.Value.Refactorings)
        {
            var registered = new List<CodeAction>();
            await RunSafelyAsync(() => provider.ComputeRefactoringsAsync(new CodeRefactoringContext(document, span, registered.Add, cancellationToken)), cancellationToken).ConfigureAwait(false);
            results.AddRange(Flatten(registered, isFix: false, solution, group: null));
        }

        // Actions ending in "..." open an options dialog in Visual Studio; without it they change nothing.
        return [.. results.Where(r => !r.Title.EndsWith("...", StringComparison.Ordinal) && !r.Title.EndsWith('…')).DistinctBy(r => (r.Group, r.Title))];
    }

    private static async Task<IReadOnlyList<CodeActionInfo>> FixAllAsync(
        Document document,
        CodeFixProvider provider,
        CodeAction action,
        Diagnostic diagnostic,
        ImmutableArray<Diagnostic> documentDiagnostics,
        Func<Document, CancellationToken, Task<ImmutableArray<Diagnostic>>> diagnosticsOf,
        CancellationToken cancellationToken)
    {
        var fixAll = provider.GetFixAllProvider();
        if (fixAll is null)
        {
            return [];
        }

        var results = new List<CodeActionInfo>();
        var scopes = fixAll.GetSupportedFixAllScopes().ToList();
        var diagnosticProvider = new AllDiagnostics(diagnosticsOf);
        var ids = new[] { diagnostic.Id };
        if (scopes.Contains(FixAllScope.Document) && documentDiagnostics.Count(d => d.Id == diagnostic.Id) > 1)
        {
            var context = new FixAllContext(document, provider, FixAllScope.Document, action.EquivalenceKey, ids, diagnosticProvider, cancellationToken);
            if (await TryGetFixAllAsync(fixAll, context, cancellationToken).ConfigureAwait(false) is { } fix)
            {
                results.Add(new CodeActionInfo(EngineText.Format("CodeAction.FixAllDocument", action.Title), diagnostic.Id, true, new Handle(fix, document.Project.Solution)));
            }
        }

        if (scopes.Contains(FixAllScope.Project))
        {
            var context = new FixAllContext(document.Project, provider, FixAllScope.Project, action.EquivalenceKey, ids, diagnosticProvider, cancellationToken);
            if (await TryGetFixAllAsync(fixAll, context, cancellationToken).ConfigureAwait(false) is { } fix)
            {
                results.Add(new CodeActionInfo(EngineText.Format("CodeAction.FixAllProject", action.Title), diagnostic.Id, true, new Handle(fix, document.Project.Solution)));
            }
        }

        return results;
    }

    private static async Task<CodeAction?> TryGetFixAllAsync(FixAllProvider fixAll, FixAllContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await fixAll.GetFixAsync(context).WaitAsync(ProviderTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<CodeActionInfo> Flatten(IEnumerable<CodeAction> actions, bool isFix, Solution solution, string? group)
    {
        foreach (var action in actions)
        {
            if (!action.NestedActions.IsEmpty)
            {
                foreach (var nested in action.NestedActions)
                {
                    yield return new CodeActionInfo(nested.Title, action.Title, isFix, new Handle(nested, solution));
                }
            }
            else
            {
                yield return new CodeActionInfo(action.Title, group ?? (isFix ? "Fix" : "Refactor"), isFix, new Handle(action, solution));
            }
        }
    }

    private static async Task RunSafelyAsync(Func<Task> run, CancellationToken cancellationToken)
    {
        try
        {
            await run().WaitAsync(ProviderTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Einzelne Provider dürfen fehlschlagen oder hängen (fehlende MEF-Dienste); die übrigen liefern weiter.
        }
    }

    private static (ImmutableArray<CodeFixProvider> Fixes, ImmutableArray<CodeRefactoringProvider> Refactorings) LoadBuiltin()
    {
        var types = new[] { "Microsoft.CodeAnalysis.CSharp.Features", "Microsoft.CodeAnalysis.Features" }
            .Select(Assembly.Load)
            .SelectMany(LoadableTypes)
            .Where(t => !t.IsAbstract && !t.ContainsGenericParameters)
            .ToList();
        return (
            [.. Create<CodeFixProvider>(types, "ExportCodeFixProviderAttribute")],
            [.. Create<CodeRefactoringProvider>(types, "ExportCodeRefactoringProviderAttribute")]);
    }

    private static IEnumerable<T> Create<T>(IEnumerable<Type> types, string exportAttribute)
        where T : class
    {
        foreach (var type in types.Where(t => typeof(T).IsAssignableFrom(t) && t.GetCustomAttributes().Any(a => a.GetType().Name == exportAttribute)))
        {
            T? instance = null;
            try
            {
                instance = Activator.CreateInstance(type, nonPublic: true) as T;
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException)
            {
                // Provider mit Konstruktor-Importen werden übersprungen.
            }

            if (instance is not null)
            {
                yield return instance;
            }
        }
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.OfType<Type>()];
        }
    }

    private sealed record Handle(CodeAction Action, Solution Solution);

    /// <summary>Supplies diagnostics for fix-all from the IDE's own analysis (compiler + active analyzers).</summary>
    private sealed class AllDiagnostics(Func<Document, CancellationToken, Task<ImmutableArray<Diagnostic>>> diagnosticsOf) : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            => await diagnosticsOf(document, cancellationToken).ConfigureAwait(false);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var all = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                all.AddRange(await diagnosticsOf(document, cancellationToken).ConfigureAwait(false));
            }

            return all;
        }
    }
}
