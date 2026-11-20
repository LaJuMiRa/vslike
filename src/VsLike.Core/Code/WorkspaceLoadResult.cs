namespace VsLike.Core.Code;

public sealed record WorkspaceLoadResult(string Path, IReadOnlyList<WorkspaceProject> Projects, IReadOnlyList<string> Warnings, TimeSpan Duration);
