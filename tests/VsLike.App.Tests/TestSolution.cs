using VsLike.Core.Files;

namespace VsLike.App.Tests;

/// <summary>Kopie der Test-Solution in einem echten (nicht verlinkten) Temp-Pfad.</summary>
internal sealed class TestSolution : IDisposable
{
    public TestSolution(string asset = "ThreeProjects")
    {
        Root = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-app-solution", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(AppContext.BaseDirectory, "TestAssets", asset);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public string Root { get; }

    public string SolutionPath => Path.Combine(Root, "Sample.slnx");

    public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
