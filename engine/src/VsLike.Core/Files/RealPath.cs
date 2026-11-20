namespace VsLike.Core.Files;

/// <summary>
/// Löst symbolische Links in allen Pfadsegmenten auf (wie realpath(3)). Nötig, weil NuGet-Restore scheitert,
/// wenn dieselbe Solution über einen Link-Pfad (z. B. macOS /var → /private/var) und den echten Pfad erreicht wird.
/// </summary>
public static class RealPath
{
    private const int MaxDepth = 40;

    public static string Resolve(string path) => Resolve(path, 0);

    private static string Resolve(string path, int depth)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "/";
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is { } target && depth < MaxDepth)
            {
                // Das Linkziel kann selbst Links enthalten (z. B. /var → /private/var), daher erneut auflösen.
                var absolute = Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(current) ?? root, target);
                current = Resolve(absolute, depth + 1);
            }
        }

        return current;
    }
}
