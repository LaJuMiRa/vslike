namespace VsLike.Core.Code;

/// <summary>Änderungen an einer Datei. Bei <see cref="IsNewFile"/> ist <see cref="NewFileText"/> der komplette Inhalt.</summary>
public sealed record FileEdit(string FilePath, IReadOnlyList<TextEdit> Edits, string? NewFileText = null)
{
    public bool IsNewFile => NewFileText is not null;
}
