namespace VsLike.Core.Search;

/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column.</param>
public sealed record TextMatch(int Offset, int Length, int Line, int Column, string LineText);
