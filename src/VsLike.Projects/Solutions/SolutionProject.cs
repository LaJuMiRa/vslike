namespace VsLike.Projects.Solutions;

/// <param name="SolutionFolder">Solution folder path such as "/libs/" or "/" for the root.</param>
public sealed record SolutionProject(string Name, string FilePath, string SolutionFolder);
