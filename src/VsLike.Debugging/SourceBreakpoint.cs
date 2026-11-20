namespace VsLike.Debugging;

/// <param name="Condition">Optional C# condition; the debugger only stops when it evaluates to true.</param>
public sealed record SourceBreakpoint(int Line, string? Condition = null);
