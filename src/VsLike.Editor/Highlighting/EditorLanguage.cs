namespace VsLike.Editor.Highlighting;

/// <param name="Id">Language id, e.g. "csharp".</param>
/// <param name="TextMateScope">TextMate grammar scope or null for plain text.</param>
/// <param name="LineComment">Line comment token or null.</param>
/// <param name="BlockComment">Block comment delimiters or null.</param>
public sealed record EditorLanguage(string Id, string? TextMateScope, string? LineComment, (string Start, string End)? BlockComment);
