namespace VsLike.Debugging;

public sealed record BreakpointState(int Line, bool Verified, string? Message);
