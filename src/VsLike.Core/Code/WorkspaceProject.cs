namespace VsLike.Core.Code;

/// <summary>Ein geladenes Projekt (Name, Pfad, Projektverweise als Pfade).</summary>
public sealed record WorkspaceProject(string Name, string FilePath, string? TargetFramework, string? RootNamespace, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> Documents);
