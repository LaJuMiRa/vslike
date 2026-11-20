namespace VsLike.Core.Code;

/// <summary>Eine zusammengehörige Änderung über mehrere Dateien (Code-Fix, Rename, Formatierung …).</summary>
/// <param name="CaretFile">File that should receive the caret afterwards, if any.</param>
/// <param name="CaretOffset">Caret offset in <paramref name="CaretFile"/> after the edits.</param>
public sealed record WorkspaceEdit(IReadOnlyList<FileEdit> Files, string? CaretFile = null, int? CaretOffset = null)
{
    public static readonly WorkspaceEdit Empty = new([]);

    public bool IsEmpty => Files.All(f => f.Edits.Count == 0 && !f.IsNewFile);
}
