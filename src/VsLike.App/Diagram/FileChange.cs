namespace VsLike.App.Diagram;

/// <param name="Before">File text before the change; null if the file was created.</param>
/// <param name="After">File text after the change; null if the file was deleted.</param>
public sealed record FileChange(string Path, string? Before, string? After);
