namespace VsLike.Editor.Snippets;

/// <summary>Ergebnis der Expansion: Text und Platzhalter-Bereiche relativ zum Snippet-Anfang.</summary>
/// <param name="FinalCaret">Offset of <c>$0</c> (or end of text).</param>
public sealed record ExpandedSnippet(string Text, IReadOnlyList<SnippetPlaceholder> Placeholders, int FinalCaret);
