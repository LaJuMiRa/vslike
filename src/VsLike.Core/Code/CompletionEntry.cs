namespace VsLike.Core.Code;

/// <summary>Ein Eintrag der Completion-Liste. <see cref="Handle"/> ist ein undurchsichtiger Verweis der Implementierung.</summary>
public sealed record CompletionEntry(
    string DisplayText,
    string FilterText,
    string SortText,
    CompletionKind Kind,
    string? InlineDescription,
    bool IsPreselected,
    object Handle);
