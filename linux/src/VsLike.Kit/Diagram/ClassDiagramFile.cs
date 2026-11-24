using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VsLike.Kit;

/// <summary>
/// Reads and writes class diagrams in the <c>.cd</c> XML format that C# IDEs keep in projects. Positions are in inches.
/// Only type positions and the collapsed state are read and written; everything else in an existing file (comments,
/// association settings, fonts) stays as it is.
/// </summary>
public static class ClassDiagramFile
{
    public const string FileExtension = "cd";

    /// <summary>Diagram coordinates per inch.</summary>
    internal const double PointsPerInch = 96.0;

    internal static readonly HashSet<string> TypeTags = ["Class", "Interface", "Struct", "Enum", "Delegate"];
    internal static readonly string PlaceholderHash = new string('A', 43) + "=";

    public static bool IsClassDiagram(string path) =>
        string.Equals(Path.GetExtension(path).TrimStart('.'), FileExtension, StringComparison.OrdinalIgnoreCase);

    public static DiagramDocument Read(byte[] data)
    {
        var xml = Parse(data);
        var types = TypeElements(xml).Select(element =>
        {
            var name = (string?)element.Attribute("Name");
            if (name is null)
            {
                return null;
            }

            var position = element.Element("Position");
            var x = ParseInches((string?)position?.Attribute("X"));
            var y = ParseInches((string?)position?.Attribute("Y"));
            var collapsed = (string?)element.Attribute("Collapsed") == "true";
            return new TypePlacement(FullName(name), x, y, collapsed);
        }).OfType<TypePlacement>().ToList();
        return new DiagramDocument(types);
    }

    /// <summary>
    /// Writes the layout into <paramref name="existing"/> (or a new file): positions of listed types are updated, new types
    /// are added, types no longer on the diagram are removed.
    /// </summary>
    public static byte[] Write(DiagramDocument document, byte[]? existing, IReadOnlyDictionary<string, TypeInfo> types)
    {
        var xml = existing is null ? NewDocument() : Parse(existing);
        var root = xml.Root ?? throw new XmlException("The class diagram has no root element.");
        var elements = new Dictionary<string, XElement>();
        foreach (var element in TypeElements(xml))
        {
            if ((string?)element.Attribute("Name") is { } name)
            {
                elements[FullName(name)] = element;
            }
        }

        var shown = document.Types.Select(t => t.FullName).ToHashSet();
        foreach (var (name, element) in elements.Where(e => !shown.Contains(e.Key)))
        {
            element.Remove();
        }

        foreach (var placement in document.Types)
        {
            // Types without a source file (external base types) cannot be listed in a .cd file.
            var element = elements.GetValueOrDefault(placement.FullName)
                ?? (types.TryGetValue(placement.FullName, out var info) ? NewElement(info, root) : null);
            if (element is not null)
            {
                Update(element, placement);
            }
        }

        return Serialize(xml);
    }

    /// <summary><c>Demo.Box&lt;TItem&gt;</c> (file) to <c>Demo.Box`1</c> (diagram model), also for nested generics.</summary>
    public static string FullName(string fileName)
    {
        var result = new StringBuilder();
        var depth = 0;
        var arity = 1;
        foreach (var character in fileName)
        {
            switch (character)
            {
                case '<':
                    depth++;
                    if (depth == 1)
                    {
                        arity = 1;
                    }

                    break;
                case '>':
                    depth--;
                    if (depth == 0)
                    {
                        result.Append('`').Append(arity.ToString(CultureInfo.InvariantCulture));
                    }

                    break;
                case ',' when depth == 1:
                    arity++;
                    break;
                default:
                    if (depth == 0)
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.ToString();
    }

    private static double ParseInches(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * PointsPerInch : double.NaN;

    private static XDocument Parse(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return XDocument.Load(stream);
    }

    private static byte[] Serialize(XDocument xml)
    {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            xml.Save(writer);
        }

        return stream.ToArray();
    }

    private static IEnumerable<XElement> TypeElements(XDocument xml) =>
        xml.Root?.Elements().Where(e => TypeTags.Contains(e.Name.LocalName)).ToList() ?? [];

    private static XDocument NewDocument() =>
        new(new XDeclaration("1.0", "utf-8", null), new XElement("ClassDiagram", new XAttribute("MajorVersion", "1"), new XAttribute("MinorVersion", "1")));

    private static XElement NewElement(TypeInfo info, XElement root)
    {
        var element = new XElement(
            info.Tag,
            new XAttribute("Name", info.Name),
            new XElement("TypeIdentifier", new XElement("HashCode", PlaceholderHash), new XElement("FileName", info.FileName)));
        root.Add(element);
        return element;
    }

    private static void Update(XElement element, TypePlacement placement)
    {
        element.SetAttributeValue("Collapsed", placement.Collapsed ? "true" : null);
        if (!placement.IsPlaced)
        {
            return;
        }

        var position = element.Element("Position");
        if (position is null)
        {
            position = new XElement("Position");
            element.AddFirst(position);
        }

        var width = (string?)position.Attribute("Width") ?? "1.5";
        position.SetAttributeValue("X", Inches(placement.X));
        position.SetAttributeValue("Y", Inches(placement.Y));
        position.SetAttributeValue("Width", width);
    }

    private static string Inches(double points)
    {
        var value = Math.Round(points / PointsPerInch * 1000, MidpointRounding.AwayFromZero) / 1000;
        return value == Math.Round(value) ? value.ToString("0.0", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What a new type element needs: its tag (<c>Class</c>, <c>Interface</c>, <c>Struct</c>, <c>Enum</c>), its name in the file
    /// (<c>Demo.Box&lt;TItem&gt;</c>) and its source file relative to the diagram (<c>Models\Box.cs</c>).
    /// </summary>
    public sealed record TypeInfo(string Tag, string Name, string FileName)
    {
        public static TypeInfo From(DiagramType type, string diagramFolder)
        {
            var tag = type.Kind switch
            {
                DiagramTypeKind.Interface => "Interface",
                DiagramTypeKind.Struct or DiagramTypeKind.RecordStruct => "Struct",
                DiagramTypeKind.Enum => "Enum",
                _ => "Class",
            };

            // `Demo.Outer.Box`1` becomes `Demo.Outer.Box<TItem>`.
            var plain = type.FullName.Split('`')[0];
            var name = type.TypeParameters.Count == 0 ? plain : $"{plain}<{string.Join(", ", type.TypeParameters.Select(t => t.Name))}>";
            var folder = Path.GetFullPath(diagramFolder).TrimEnd('/') + "/";
            var path = type.FilePaths.Count > 0 ? type.FilePaths[0] : string.Empty;
            var fileName = (path.StartsWith(folder, StringComparison.Ordinal) ? path[folder.Length..] : Path.GetFileName(path)).Replace('/', '\\');
            return new TypeInfo(tag, name, fileName);
        }
    }
}
