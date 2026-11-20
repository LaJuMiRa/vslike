namespace VsLike.Core.Files;

/// <summary>Liest einen Ordner rekursiv ein; Build-Ausgaben und Werkzeugordner werden ausgelassen.</summary>
public static class FileTreeBuilder
{
    public static readonly IReadOnlySet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "TestResults", ".DS_Store",
    };

    public static FileTreeNode Build(string rootPath, int maxDepth = 32)
    {
        var info = new DirectoryInfo(rootPath);
        return new FileTreeNode(info.Name, info.FullName, IsDirectory: true, ReadChildren(info, maxDepth));
    }

    public static bool IsIgnored(string rootPath, string fullPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IgnoredDirectories.Contains);
    }

    private static IReadOnlyList<FileTreeNode> ReadChildren(DirectoryInfo directory, int depth)
    {
        if (depth <= 0)
        {
            return [];
        }

        try
        {
            var directories = directory.EnumerateDirectories()
                .Where(d => !IgnoredDirectories.Contains(d.Name) && (!d.Attributes.HasFlag(FileAttributes.Hidden) || d.Name == ".vslike"))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FileTreeNode(d.Name, d.FullName, IsDirectory: true, ReadChildren(d, depth - 1)));
            var files = directory.EnumerateFiles()
                .Where(f => !IgnoredDirectories.Contains(f.Name))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FileTreeNode(f.Name, f.FullName, IsDirectory: false, []));
            return [.. directories, .. files];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
