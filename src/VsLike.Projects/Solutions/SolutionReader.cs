using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VsLike.Projects.Solutions;

/// <summary>Liest .slnx (XML) und .sln (Textformat) inklusive Solution-Ordnern.</summary>
public static partial class SolutionReader
{
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public static SolutionModel Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var text = File.ReadAllText(fullPath);
        return string.Equals(Path.GetExtension(fullPath), ".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(fullPath, text)
            : ParseSln(fullPath, text);
    }

    public static SolutionModel ParseSlnx(string path, string text)
    {
        var directory = Path.GetDirectoryName(path)!;
        var root = XDocument.Parse(text).Root ?? throw new FormatException("Empty .slnx file.");
        var projects = new List<SolutionProject>();
        var folders = new List<string>();
        Visit(root, "/");
        return new SolutionModel(path, SolutionFormat.Slnx, projects, folders);

        void Visit(XElement element, string folder)
        {
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "Folder" && child.Attribute("Name")?.Value is { } name)
                {
                    folders.Add(name);
                    Visit(child, name);
                }
                else if (child.Name.LocalName == "Project" && child.Attribute("Path")?.Value is { } relative)
                {
                    var file = Path.GetFullPath(Path.Combine(directory, relative.Replace('\\', Path.DirectorySeparatorChar)));
                    projects.Add(new SolutionProject(Path.GetFileNameWithoutExtension(file), file, folder));
                }
            }
        }
    }

    public static SolutionModel ParseSln(string path, string text)
    {
        var directory = Path.GetDirectoryName(path)!;
        var entries = ProjectLine().Matches(text)
            .Select(m => (Type: m.Groups["type"].Value, Name: m.Groups["name"].Value, Path: m.Groups["path"].Value, Guid: m.Groups["guid"].Value.ToUpperInvariant()))
            .ToList();
        var parents = NestedLine().Matches(text).ToDictionary(m => m.Groups["child"].Value.ToUpperInvariant(), m => m.Groups["parent"].Value.ToUpperInvariant());
        var folderNames = entries.Where(e => e.Type.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase)).ToDictionary(e => e.Guid, e => e.Name);

        string FolderPath(string guid)
        {
            var segments = new List<string>();
            var current = guid;
            while (parents.TryGetValue(current, out var parent) && folderNames.TryGetValue(parent, out var name))
            {
                segments.Insert(0, name);
                current = parent;
            }

            return segments.Count == 0 ? "/" : "/" + string.Join('/', segments) + "/";
        }

        var projects = entries
            .Where(e => !e.Type.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
            .Select(e => new SolutionProject(e.Name, Path.GetFullPath(Path.Combine(directory, e.Path.Replace('\\', Path.DirectorySeparatorChar))), FolderPath(e.Guid)))
            .ToList();
        var folders = folderNames.Keys.Select(g => FolderPath(g) + folderNames[g] + "/").Select(f => f.Replace("//", "/", StringComparison.Ordinal)).ToList();
        return new SolutionModel(path, SolutionFormat.Sln, projects, folders);
    }

    [GeneratedRegex(@"^Project\(""\{(?<type>[^}]+)\}""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+)"",\s*""\{(?<guid>[^}]+)\}""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    [GeneratedRegex(@"^\s*\{(?<child>[0-9A-Fa-f-]+)\}\s*=\s*\{(?<parent>[0-9A-Fa-f-]+)\}", RegexOptions.Multiline)]
    private static partial Regex NestedLine();
}
