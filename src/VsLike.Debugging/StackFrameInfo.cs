namespace VsLike.Debugging;

public sealed record StackFrameInfo(int Id, string Name, string? FilePath, int Line, int Column);
