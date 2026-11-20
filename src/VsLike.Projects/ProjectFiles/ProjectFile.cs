using System.Xml.Linq;

namespace VsLike.Projects.ProjectFiles;

/// <summary>
/// Liest und schreibt Eigenschaften einer csproj direkt im XML. Leerraum und Kommentare bleiben erhalten;
/// neue Eigenschaften landen in der ersten PropertyGroup ohne Bedingung.
/// </summary>
public sealed class ProjectFile
{
    public static readonly IReadOnlyList<string> EditableProperties =
    [
        "TargetFramework", "OutputType", "RootNamespace", "AssemblyName", "Nullable", "ImplicitUsings", "LangVersion",
        "TreatWarningsAsErrors", "Version", "Authors", "Description", "GenerateDocumentationFile", "InvariantGlobalization",
    ];

    private readonly XDocument _document;

    private ProjectFile(string path, XDocument document)
    {
        Path = path;
        _document = document;
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    private XNamespace Ns => _document.Root?.Name.Namespace ?? XNamespace.None;

    public static ProjectFile Load(string path) => new(path, XDocument.Load(path, LoadOptions.PreserveWhitespace));

    public static ProjectFile Parse(string path, string xml) => new(path, XDocument.Parse(xml, LoadOptions.PreserveWhitespace));

    public string? GetProperty(string name)
        => UnconditionalGroups().SelectMany(g => g.Elements()).LastOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>Sets a property; null or empty removes it.</summary>
    public void SetProperty(string name, string? value)
    {
        var existing = UnconditionalGroups().SelectMany(g => g.Elements()).LastOrDefault(e => e.Name.LocalName == name);
        if (string.IsNullOrEmpty(value))
        {
            RemoveWithWhitespace(existing);
            return;
        }

        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        var group = UnconditionalGroups().FirstOrDefault() ?? CreateGroup();
        var indent = (group.Elements().FirstOrDefault()?.PreviousNode as XText)?.Value ?? "\n    ";
        var closing = group.LastNode as XText;
        var element = new XElement(Ns + name, value);
        if (closing is not null && string.IsNullOrWhiteSpace(closing.Value))
        {
            closing.AddBeforeSelf(new XText(indent), element);
        }
        else
        {
            group.Add(new XText(indent), element, new XText("\n  "));
        }
    }

    public IReadOnlyList<string> ProjectReferences()
        => _document.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(r => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, r.Replace('\\', System.IO.Path.DirectorySeparatorChar))))
            .ToList();

    public IReadOnlyList<(string Id, string? Version)> PackageReferences()
        => _document.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (Id: e.Attribute("Include")?.Value ?? string.Empty, Version: e.Attribute("Version")?.Value ?? e.Element(Ns + "Version")?.Value))
            .Where(p => p.Id.Length > 0)
            .ToList();

    public string ToXml() => _document.Declaration is null ? _document.ToString(SaveOptions.DisableFormatting) : _document.Declaration + _document.ToString(SaveOptions.DisableFormatting);

    public void Save() => File.WriteAllText(Path, ToXml());

    private static void RemoveWithWhitespace(XElement? element)
    {
        if (element is null)
        {
            return;
        }

        if (element.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
        {
            whitespace.Remove();
        }

        element.Remove();
    }

    private IEnumerable<XElement> UnconditionalGroups()
        => _document.Root?.Elements().Where(e => e.Name.LocalName == "PropertyGroup" && e.Attribute("Condition") is null) ?? [];

    private XElement CreateGroup()
    {
        var group = new XElement(Ns + "PropertyGroup", new XText("\n  "));
        _document.Root!.AddFirst(new XText("\n  "), group);
        return group;
    }
}
