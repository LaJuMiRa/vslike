namespace VsLike.Core.Code;

/// <summary>Hover-Information: Signatur, Dokumentation und Diagnosen an der Position.</summary>
public sealed record QuickInfo(int Start, int Length, string Signature, string? Documentation, IReadOnlyList<DiagnosticInfo> Diagnostics);
