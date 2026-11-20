using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;

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

    public async Task<IReadOnlyList<CodeActionInfo>> GetAsync(Document document, TextSpan span, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
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
            }
        }

        foreach (var provider in Builtin.Value.Refactorings)
        {
            var registered = new List<CodeAction>();
            await RunSafelyAsync(() => provider.ComputeRefactoringsAsync(new CodeRefactoringContext(document, span, registered.Add, cancellationToken)), cancellationToken).ConfigureAwait(false);
            results.AddRange(Flatten(registered, isFix: false, solution, group: null));
        }

        return [.. results.DistinctBy(r => (r.Group, r.Title))];
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
}
