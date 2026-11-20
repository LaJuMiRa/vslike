namespace VsLike.Editor.Snippets;

/// <param name="Shortcut">Word that expands the snippet with Tab, e.g. "prop".</param>
/// <param name="Body">Template: <c>${1:default}</c> placeholders (same number = linked), <c>$0</c> final caret, <c>$TYPE</c> enclosing type name.</param>
public sealed record SnippetDefinition(string Shortcut, string Description, string Body);
