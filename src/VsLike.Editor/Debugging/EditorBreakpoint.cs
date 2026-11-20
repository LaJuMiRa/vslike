namespace VsLike.Editor.Debugging;

/// <param name="Line">1-based line.</param>
public sealed record EditorBreakpoint(int Line, string? Condition = null, bool Verified = true);
