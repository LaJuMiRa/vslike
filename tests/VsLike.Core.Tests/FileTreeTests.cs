using VsLike.Core.Files;

namespace VsLike.Core.Tests;

public sealed class FileTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vslike-tree", Guid.NewGuid().ToString("N"));

    public FileTreeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "bin"));
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "src", "Program.cs"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "src", "bin", "x.dll"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "App.slnx"), string.Empty);
    }

    [Fact]
    public void Build_SkipsBuildOutputAndSortsFoldersFirst()
    {
        var tree = FileTreeBuilder.Build(_root);

        Assert.Equal(["src", "App.slnx"], tree.Children.Select(c => c.Name));
        Assert.Equal(["Program.cs"], tree.Children[0].Children.Select(c => c.Name));
    }

    [Fact]
    public void IsIgnored_DetectsIgnoredSegments()
    {
        Assert.True(FileTreeBuilder.IsIgnored(_root, Path.Combine(_root, "src", "obj", "a.cs")));
        Assert.False(FileTreeBuilder.IsIgnored(_root, Path.Combine(_root, "src", "a.cs")));
    }

    [Fact]
    public async Task FolderWatcher_ReportsCreatedFilesOnceDebounced()
    {
        using var watcher = new FolderWatcher(_root, TimeSpan.FromMilliseconds(100));
        var received = new TaskCompletionSource<IReadOnlyList<string>>();
        watcher.Changed += (_, paths) => received.TrySetResult(paths);

        File.WriteAllText(Path.Combine(_root, "src", "New.cs"), "class New {}");
        File.WriteAllText(Path.Combine(_root, "obj", "ignored.cs"), string.Empty);

        var paths = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(paths, p => p.EndsWith("New.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.Contains("ignored", StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
