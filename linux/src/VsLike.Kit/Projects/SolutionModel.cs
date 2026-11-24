using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VsLike.Kit;

public enum SolutionFormat
{
    Sln,
    Slnx,
}

/// <param name="SolutionFolder">Solution folder such as <c>/src/</c>, <c>/</c> for the root.</param>
public sealed record SolutionProject(string Name, string FilePath, string SolutionFolder)
{
    public string Id => FilePath;

    public string Directory => Path.GetDirectoryName(FilePath) ?? "/";
}

public sealed record SolutionModel(string FilePath, SolutionFormat Format, IReadOnlyList<SolutionProject> Projects, IReadOnlyList<string> Folders)
{
    public string Id => FilePath;

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public string Directory => Path.GetDirectoryName(FilePath) ?? "/";

    public bool Equals(SolutionModel? other) =>
        other is not null && FilePath == other.FilePath && Format == other.Format && Projects.SequenceEqual(other.Projects) && Folders.SequenceEqual(other.Folders);

    public override int GetHashCode() => HashCode.Combine(FilePath, Format, Projects.Count);
}

/// <summary>Reads <c>.slnx</c> (XML) and classic <c>.sln</c> files.</summary>
public static partial class SolutionReader
{
    internal const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public static SolutionModel Read(string path)
    {
        var text = File.ReadAllText(path);
        return Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? ParseSlnx(path, text) : ParseSln(path, text);
    }

    public static SolutionModel ParseSlnx(string path, string text)
    {
        var directory = Path.GetDirectoryName(path) ?? "/";
        var document = XDocument.Parse(text);
        var projects = new List<SolutionProject>();
        var folders = new List<string>();
        void Visit(XElement element, string folder)
        {
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "Folder" && (string?)child.Attribute("Name") is { } name)
                {
                    folders.Add(name);
                    Visit(child, name);
                }
                else if (child.Name.LocalName == "Project" && (string?)child.Attribute("Path") is { } relative)
                {
                    var file = Path.GetFullPath(Path.Combine(directory, relative.Replace('\\', '/')));
                    projects.Add(new SolutionProject(Path.GetFileNameWithoutExtension(file), file, folder));
                }
            }
        }

        if (document.Root is { } root)
        {
            Visit(root, "/");
        }

        return new SolutionModel(path, SolutionFormat.Slnx, projects, folders);
    }

    public static SolutionModel ParseSln(string path, string text)
    {
        var directory = Path.GetDirectoryName(path) ?? "/";
        var entries = ProjectLine().Matches(text).Select(m => (
            Type: m.Groups["type"].Value.ToUpperInvariant(),
            Name: m.Groups["name"].Value,
            Path: m.Groups["path"].Value,
            Guid: m.Groups["guid"].Value.ToUpperInvariant())).ToList();
        var parents = new Dictionary<string, string>();
        foreach (Match match in NestedLine().Matches(text))
        {
            parents[match.Groups["child"].Value.ToUpperInvariant()] = match.Groups["parent"].Value.ToUpperInvariant();
        }

        var folderNames = new Dictionary<string, string>();
        foreach (var entry in entries.Where(e => e.Type == SolutionFolderType))
        {
            folderNames.TryAdd(entry.Guid, entry.Name);
        }

        string FolderPath(string guid)
        {
            var segments = new List<string>();
            var current = guid;
            while (parents.TryGetValue(current, out var parent) && folderNames.TryGetValue(parent, out var name))
            {
                segments.Insert(0, name);
                current = parent;
            }

            return segments.Count == 0 ? "/" : "/" + string.Join("/", segments) + "/";
        }

        var projects = entries
            .Where(e => e.Type != SolutionFolderType && e.Path.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
            .Select(e => new SolutionProject(e.Name, Path.GetFullPath(Path.Combine(directory, e.Path.Replace('\\', '/'))), FolderPath(e.Guid)))
            .ToList();
        var folders = folderNames.Keys.Select(g => (FolderPath(g) + folderNames[g] + "/").Replace("//", "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        return new SolutionModel(path, SolutionFormat.Sln, projects, folders);
    }

    [GeneratedRegex(""""^Project\("\{(?<type>[^}]+)\}"\)\s*=\s*"(?<name>[^"]+)",\s*"(?<path>[^"]+)",\s*"\{(?<guid>[^}]+)\}"""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    [GeneratedRegex(@"^\s*\{(?<child>[0-9A-Fa-f-]+)\}\s*=\s*\{(?<parent>[0-9A-Fa-f-]+)\}", RegexOptions.Multiline)]
    private static partial Regex NestedLine();
}
