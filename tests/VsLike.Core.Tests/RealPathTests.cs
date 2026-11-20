using VsLike.Core.Files;

namespace VsLike.Core.Tests;

public sealed class RealPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vslike-realpath", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_FollowsDirectorySymlinkInMiddleOfPath()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target"));
        File.WriteAllText(Path.Combine(target.FullName, "a.slnx"), string.Empty);
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), target.FullName);

        var resolved = RealPath.Resolve(Path.Combine(_root, "link", "a.slnx"));

        Assert.Equal(Path.Combine(RealPath.Resolve(target.FullName), "a.slnx"), resolved);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "link" + Path.DirectorySeparatorChar, resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_KeepsNonExistingTail()
    {
        Directory.CreateDirectory(_root);

        Assert.EndsWith(Path.Combine("missing", "x.cs"), RealPath.Resolve(Path.Combine(_root, "missing", "x.cs")), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
