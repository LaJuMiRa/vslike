namespace VsLike.Core.Code;

/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
/// <param name="LineText">Text of the line (for reference lists).</param>
public sealed record SourceLocation(string FilePath, int Start, int Length, int Line, int Column, string LineText);
