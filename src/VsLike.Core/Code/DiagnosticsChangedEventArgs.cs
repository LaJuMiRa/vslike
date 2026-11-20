namespace VsLike.Core.Code;

/// <summary>Neue Diagnosen für eine Datei (oder für alle Dateien, wenn <see cref="FilePath"/> null ist).</summary>
public sealed class DiagnosticsChangedEventArgs(string? filePath, IReadOnlyList<DiagnosticInfo> diagnostics) : EventArgs
{
    public string? FilePath { get; } = filePath;

    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; } = diagnostics;
}
