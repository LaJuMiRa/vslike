namespace VsLike.Projects.Solutions;

public sealed record SolutionModel(string FilePath, SolutionFormat Format, IReadOnlyList<SolutionProject> Projects, IReadOnlyList<string> Folders)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public string Directory => Path.GetDirectoryName(FilePath)!;
}
