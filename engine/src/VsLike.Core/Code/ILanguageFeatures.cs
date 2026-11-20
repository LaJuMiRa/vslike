namespace VsLike.Core.Code;

/// <summary>
/// IntelliSense und Code-Navigation. Ergebnisse, die Code ändern, kommen als <see cref="WorkspaceEdit"/>
/// zurück; die IDE wendet sie auf Editor-Tabs bzw. Dateien an.
/// </summary>
public interface ILanguageFeatures
{
    Task<bool> ShouldTriggerCompletionAsync(string filePath, int offset, char typed, CancellationToken cancellationToken);

    Task<CompletionList?> GetCompletionAsync(string filePath, int offset, CancellationToken cancellationToken);

    Task<string?> GetCompletionDescriptionAsync(string filePath, CompletionEntry entry, CancellationToken cancellationToken);

    /// <summary>Returns the edits for committing an entry (includes e.g. an added using directive).</summary>
    Task<WorkspaceEdit> ResolveCompletionAsync(string filePath, CompletionEntry entry, CancellationToken cancellationToken);

    Task<SignatureHelp?> GetSignatureHelpAsync(string filePath, int offset, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(string filePath, int start, int length, CancellationToken cancellationToken);

    Task<WorkspaceEdit> GetCodeActionEditAsync(CodeActionInfo action, CancellationToken cancellationToken);

    Task<WorkspaceEdit> FormatDocumentAsync(string filePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceLocation>> GoToDefinitionAsync(string filePath, int offset, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(string filePath, int offset, CancellationToken cancellationToken);

    Task<RenameTarget?> PrepareRenameAsync(string filePath, int offset, CancellationToken cancellationToken);

    Task<WorkspaceEdit> RenameAsync(string filePath, int offset, string newName, CancellationToken cancellationToken);

    /// <summary>Finds the source declaration of a type (full name) or one of its members, e.g. for the test explorer.</summary>
    Task<SourceLocation?> FindDeclarationAsync(string typeName, string? memberName, CancellationToken cancellationToken);

    /// <summary>Returns the name of the type enclosing the offset (for snippets such as ctor).</summary>
    Task<string?> GetEnclosingTypeNameAsync(string filePath, int offset, CancellationToken cancellationToken);
}
