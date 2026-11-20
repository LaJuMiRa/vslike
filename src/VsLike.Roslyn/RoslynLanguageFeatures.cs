using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;
using VsLike.Roslyn.Features;
using CompletionList = VsLike.Core.Code.CompletionList;

namespace VsLike.Roslyn;

/// <summary><see cref="ILanguageFeatures"/> auf Basis des <see cref="RoslynWorkspace"/>.</summary>
public sealed class RoslynLanguageFeatures : ILanguageFeatures
{
    private readonly RoslynWorkspace _workspace;

    public RoslynLanguageFeatures(RoslynWorkspace workspace)
    {
        _workspace = workspace;
    }

    internal CodeActionFeature CodeActions { get; } = new();

    public Task<bool> ShouldTriggerCompletionAsync(string filePath, int offset, char typed, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? CompletionFeature.ShouldTriggerAsync(document, offset, typed, cancellationToken) : Task.FromResult(false);

    public Task<CompletionList?> GetCompletionAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? CompletionFeature.GetAsync(document, offset, cancellationToken) : Task.FromResult<CompletionList?>(null);

    public Task<string?> GetCompletionDescriptionAsync(string filePath, CompletionEntry entry, CancellationToken cancellationToken)
        => CompletionFeature.DescribeAsync(entry, cancellationToken);

    public Task<WorkspaceEdit> ResolveCompletionAsync(string filePath, CompletionEntry entry, CancellationToken cancellationToken)
        => CompletionFeature.ResolveAsync(entry, cancellationToken);

    public Task<SignatureHelp?> GetSignatureHelpAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? SignatureHelpFeature.GetAsync(document, offset, cancellationToken) : Task.FromResult<SignatureHelp?>(null);

    public async Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(string filePath, int start, int length, CancellationToken cancellationToken)
    {
        if (_workspace.GetDocument(filePath) is not { } document)
        {
            return [];
        }

        var span = new TextSpan(start, length);
        var diagnostics = await _workspace.GetRoslynDiagnosticsAsync(document, cancellationToken).ConfigureAwait(false);
        return await CodeActions.GetAsync(document, span, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    public Task<WorkspaceEdit> GetCodeActionEditAsync(CodeActionInfo action, CancellationToken cancellationToken)
        => CodeActionFeature.GetEditAsync(action, cancellationToken);

    public async Task<WorkspaceEdit> FormatDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_workspace.GetDocument(filePath) is not { } document)
        {
            return WorkspaceEdit.Empty;
        }

        var formatted = await Formatter.FormatAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        var changes = await formatted.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false);
        return new WorkspaceEdit([new FileEdit(filePath, [.. changes.Select(c => new TextEdit(c.Span.Start, c.Span.Length, c.NewText ?? string.Empty))])]);
    }

    public Task<IReadOnlyList<SourceLocation>> GoToDefinitionAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? NavigationFeature.GoToDefinitionAsync(document, offset, cancellationToken) : Task.FromResult<IReadOnlyList<SourceLocation>>([]);

    public Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? NavigationFeature.FindReferencesAsync(document, offset, cancellationToken) : Task.FromResult<IReadOnlyList<SourceLocation>>([]);

    public Task<RenameTarget?> PrepareRenameAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? NavigationFeature.PrepareRenameAsync(document, offset, cancellationToken) : Task.FromResult<RenameTarget?>(null);

    public Task<WorkspaceEdit> RenameAsync(string filePath, int offset, string newName, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? NavigationFeature.RenameAsync(document, offset, newName, cancellationToken) : Task.FromResult(WorkspaceEdit.Empty);

    public Task<SourceLocation?> FindDeclarationAsync(string typeName, string? memberName, CancellationToken cancellationToken)
        => _workspace.CurrentSolution is { } solution ? NavigationFeature.FindDeclarationAsync(solution, typeName, memberName, cancellationToken) : Task.FromResult<SourceLocation?>(null);

    public Task<string?> GetEnclosingTypeNameAsync(string filePath, int offset, CancellationToken cancellationToken)
        => _workspace.GetDocument(filePath) is { } document ? NavigationFeature.EnclosingTypeNameAsync(document, offset, cancellationToken) : Task.FromResult<string?>(null);
}
