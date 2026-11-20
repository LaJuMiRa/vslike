namespace VsLike.Debugging;

public sealed record StoppedInfo(string Reason, int ThreadId, string? Description);
