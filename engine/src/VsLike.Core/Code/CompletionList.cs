namespace VsLike.Core.Code;

/// <param name="SpanStart">Start of the text the list replaces (the typed prefix).</param>
/// <param name="SpanLength">Length of the typed prefix.</param>
public sealed record CompletionList(IReadOnlyList<CompletionEntry> Items, int SpanStart, int SpanLength);
