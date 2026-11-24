using System.Globalization;
using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>A node of the solution tree: solution folders, projects, folders and files.</summary>
/// <param name="Path">The file or folder (null for solution folders, the dependencies node and packages).</param>
public sealed record FileNode(string Id, string Name, string? Path, FileNode.NodeKind Kind, IReadOnlyList<FileNode>? Children = null)
{
    public enum KindTag
    {
        SolutionFolder,
        Project,
        Folder,
        File,
        Dependencies,
        Reference,
        Package,
    }

    /// <summary>Gets the SF Symbols name used on macOS; the views map it to an icon.</summary>
    public string Symbol => Kind.Tag switch
    {
        KindTag.SolutionFolder => "folder.fill.badge.gearshape",
        KindTag.Project => Kind.IsTest ? "checklist" : "shippingbox.fill",
        KindTag.Folder => "folder.fill",
        KindTag.Dependencies => "link",
        KindTag.Reference => "arrow.triangle.branch",
        KindTag.Package => "cube.box",
        _ => SymbolForFile(Name),
    };

    public static string SymbolForFile(string name)
    {
        if (DiagramDocument.IsDiagram(name))
        {
            return "rectangle.3.group";
        }

        return System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant() switch
        {
            "cs" => "c.square",
            "csproj" or "slnx" or "sln" or "props" or "targets" => "gearshape",
            "json" => "curlybraces",
            "md" => "doc.richtext",
            "xml" or "config" or "resx" or "xaml" or "axaml" => "chevron.left.forwardslash.chevron.right",
            "png" or "jpg" or "jpeg" or "svg" or "ico" => "photo",
            _ => "doc.text",
        };
    }

    public bool Equals(FileNode? other) =>
        other is not null && Id == other.Id && Name == other.Name && Path == other.Path && Kind == other.Kind
        && (Children is null ? other.Children is null : other.Children is not null && Children.SequenceEqual(other.Children));

    public override int GetHashCode() => HashCode.Combine(Id, Name, Path, Kind);

    /// <summary>The kind of a node, with the data of the Swift enum's associated values.</summary>
    public sealed record NodeKind(KindTag Tag, bool IsTest = false, string? ReferencePath = null, string? PackageId = null, string? PackageVersion = null)
    {
        public static readonly NodeKind SolutionFolder = new(KindTag.SolutionFolder);
        public static readonly NodeKind Folder = new(KindTag.Folder);
        public static readonly NodeKind File = new(KindTag.File);
        public static readonly NodeKind Dependencies = new(KindTag.Dependencies);

        public static NodeKind Project(bool isTest) => new(KindTag.Project, IsTest: isTest);

        public static NodeKind Reference(string path) => new(KindTag.Reference, ReferencePath: path);

        public static NodeKind Package(string id, string? version) => new(KindTag.Package, PackageId: id, PackageVersion: version);
    }
}

/// <summary>Builds the tree shown in the sidebar from the solution (or a plain folder) and the file system.</summary>
public static class FileTreeBuilder
{
    public static readonly HashSet<string> Ignored = ["bin", "obj", ".git", ".vs", ".vslike", ".idea", "node_modules", "artifacts", ".DS_Store", "TestResults"];

    /// <summary>Gets Finder order (<c>localizedStandardCompare</c>): case-insensitive, numbers by value.</summary>
    public static IComparer<string> StandardOrder { get; } = Comparer<string>.Create(StandardCompare);

    public static IReadOnlyList<FileNode> SolutionTree(SolutionModel solution)
    {
        var folders = new Dictionary<string, List<FileNode>>();
        foreach (var project in solution.Projects)
        {
            if (!folders.TryGetValue(project.SolutionFolder, out var list))
            {
                folders[project.SolutionFolder] = list = [];
            }

            list.Add(ProjectNode(project));
        }

        List<FileNode> Build(string path)
        {
            var childFolders = solution.Folders.Concat(folders.Keys).ToHashSet()
                .Where(f => f != path && f.StartsWith(path, StringComparison.Ordinal) && !f[path.Length..^1].Contains('/', StringComparison.Ordinal));
            var folderNodes = childFolders.Order(StringComparer.Ordinal)
                .Select(f => new FileNode("sf:" + f, f[path.Length..^1], null, FileNode.NodeKind.SolutionFolder, Build(f)));
            var projects = folders.GetValueOrDefault(path)?.OrderBy(n => n.Name, StandardOrder) ?? Enumerable.Empty<FileNode>();
            return [.. folderNodes, .. projects];
        }

        var topLevelFiles = Files(solution.Directory, 0).Where(n => n.Kind.Tag == FileNode.KindTag.File);
        var root = Build("/");
        return [.. root, .. topLevelFiles.Where(n => (!n.Name.EndsWith(".sln", StringComparison.Ordinal) && !n.Name.EndsWith(".slnx", StringComparison.Ordinal)) || n.Path == solution.FilePath)];
    }

    public static FileNode ProjectNode(SolutionProject project)
    {
        ProjectFile? file;
        try
        {
            file = ProjectFile.Load(project.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            file = null;
        }

        var dependencies = new List<FileNode>();
        foreach (var reference in file?.ProjectReferences() ?? [])
        {
            var name = Path.GetFileNameWithoutExtension(reference);
            dependencies.Add(new FileNode($"ref:{project.FilePath}>{reference}", name, reference, FileNode.NodeKind.Reference(reference)));
        }

        foreach (var (id, version) in file?.PackageReferences() ?? [])
        {
            var label = version is null ? id : $"{id} {version}";
            dependencies.Add(new FileNode($"pkg:{project.FilePath}>{id}", label, null, FileNode.NodeKind.Package(id, version)));
        }

        var children = new List<FileNode> { new("deps:" + project.FilePath, L.T("Dependencies"), null, FileNode.NodeKind.Dependencies, dependencies) };
        children.AddRange(Files(project.Directory, 0));
        return new FileNode("proj:" + project.FilePath, project.Name, project.FilePath, FileNode.NodeKind.Project(file?.IsTestProject ?? false), children);
    }

    /// <summary>Folders first, then files, both in Finder order; build output and hidden folders are skipped.</summary>
    public static IReadOnlyList<FileNode> Files(string directory, int depth)
    {
        if (depth >= 12 || !Directory.Exists(directory))
        {
            return [];
        }

        string[] names;
        try
        {
            names = [.. Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var folders = new List<FileNode>();
        var files = new List<FileNode>();
        foreach (var name in names.Where(n => !Ignored.Contains(n) && !n.StartsWith('.')))
        {
            var path = Path.Combine(directory, name);
            if (Directory.Exists(path))
            {
                folders.Add(new FileNode(path, name, path, FileNode.NodeKind.Folder, Files(path, depth + 1)));
            }
            else if (!name.EndsWith(".csproj", StringComparison.Ordinal))
            {
                files.Add(new FileNode(path, name, path, FileNode.NodeKind.File));
            }
        }

        return [.. folders.OrderBy(n => n.Name, StandardOrder), .. files.OrderBy(n => n.Name, StandardOrder)];
    }

    /// <summary>All files below the root (for quick open and find in files).</summary>
    public static IReadOnlyList<FileNode> AllFiles(IEnumerable<FileNode> nodes) =>
        nodes.SelectMany(node => node.Kind.Tag switch
        {
            FileNode.KindTag.File => [node],
            FileNode.KindTag.Dependencies => [],
            _ => AllFiles(node.Children ?? []),
        }).ToList();

    internal static int StandardCompare(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return string.CompareOrdinal(left, right);
        }

        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < left.Length && char.IsDigit(left[i]))
                {
                    i++;
                }

                while (j < right.Length && char.IsDigit(right[j]))
                {
                    j++;
                }

                var a = left[startI..i].TrimStart('0');
                var b = right[startJ..j].TrimStart('0');
                var byNumber = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var byText = string.Compare(left[i].ToString(), right[j].ToString(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
            if (byText != 0)
            {
                return byText;
            }

            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }
}

/// <summary>
/// Watches a folder tree (inotify through <see cref="FileSystemWatcher"/>, FSEvents on macOS) and reports changed
/// paths on the UI thread. The workspace coalesces bursts.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action<IReadOnlyList<string>> _handler;
    private bool _stopped;

    public FolderWatcher(string path, Action<IReadOnlyList<string>> handler)
    {
        _handler = handler;
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += (_, e) => Report(e.FullPath);
            _watcher.Created += (_, e) => Report(e.FullPath);
            _watcher.Deleted += (_, e) => Report(e.FullPath);
            _watcher.Renamed += (_, e) => Report(e.OldFullPath, e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Too many watches (inotify limit) or a vanished folder: the tree still refreshes on explicit actions.
            _watcher = null;
        }
    }

    public void Stop()
    {
        _stopped = true;
        _watcher?.Dispose();
    }

    public void Dispose() => Stop();

    private void Report(params string[] paths)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Events already posted for a replaced or stopped watcher are dropped.
            if (!_stopped)
            {
                _handler(paths);
            }
        });
    }
}
