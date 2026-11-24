using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VsLike.Kit;

/// <summary>A <c>.csproj</c> file: reads and writes unconditional properties and lists references, keeping the rest of the file.</summary>
public sealed partial record ProjectFile(string Path, string Text)
{
    public static readonly IReadOnlyList<string> EditableProperties =
    [
        "TargetFramework", "OutputType", "RootNamespace", "AssemblyName", "Nullable", "ImplicitUsings", "LangVersion",
        "TreatWarningsAsErrors", "Version", "Authors", "Description", "GenerateDocumentationFile", "InvariantGlobalization",
    ];

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "/";

    public bool IsTestProject
    {
        get
        {
            string[] testPackages = ["microsoft.net.test.sdk", "mstest.sdk", "xunit.v3", "tunit"];
            return string.Equals(Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
                || PackageReferences().Any(p => testPackages.Contains(p.Id.ToLowerInvariant()))
                || Text.Contains("Sdk=\"MSTest.Sdk", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static ProjectFile Load(string path) => new(path, File.ReadAllText(path));

    /// <summary>The last value of an unconditional <c>&lt;PropertyGroup&gt;</c> property.</summary>
    public string? Property(string name)
    {
        var match = UnconditionalGroups().SelectMany(g => PropertyMatches(name, g)).LastOrDefault();
        return match.Value.Length > 0 || match.Element.Length > 0 ? Text.Substring(match.Value.Start, match.Value.Length) : null;
    }

    /// <summary>Returns a copy with the property set (or with null/empty removed), editing only that element's text.</summary>
    public ProjectFile SetProperty(string name, string? value)
    {
        var escaped = (value ?? string.Empty).Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        var groups = UnconditionalGroups();
        var matches = groups.SelectMany(g => PropertyMatches(name, g)).ToList();
        if (matches.Count > 0)
        {
            var existing = matches[^1];
            if (escaped.Length == 0)
            {
                var start = existing.Element.Start;
                while (start > 0 && (Text[start - 1] == ' ' || Text[start - 1] == '\t'))
                {
                    start--;
                }

                if (start > 0 && Text[start - 1] == '\n')
                {
                    start--;
                }

                return this with { Text = Text.Remove(start, existing.Element.End - start) };
            }

            return this with { Text = Text.Remove(existing.Value.Start, existing.Value.Length).Insert(existing.Value.Start, escaped) };
        }

        if (escaped.Length == 0)
        {
            return this;
        }

        if (groups.Count > 0)
        {
            var group = groups[0];
            var close = Text.IndexOf("</PropertyGroup>", group.Start, group.Length, StringComparison.Ordinal);
            var lineStart = close;
            while (lineStart > 0 && Text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            var closingIndent = Text[lineStart..close];
            return this with { Text = Text.Insert(lineStart, $"  {closingIndent}<{name}>{escaped}</{name}>\n") };
        }

        var project = ProjectElement().Match(Text);
        return project.Success
            ? this with { Text = Text.Insert(project.Index + project.Length, $"\n  <PropertyGroup>\n    <{name}>{escaped}</{name}>\n  </PropertyGroup>\n") }
            : this;
    }

    public IReadOnlyList<string> ProjectReferences() =>
        [.. AttributeValues("ProjectReference", "Include").Select(r => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, r.Replace('\\', '/'))))];

    public IReadOnlyList<(string Id, string? Version)> PackageReferences()
    {
        var document = TryParse();
        if (document is null)
        {
            return [];
        }

        return document.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value))
            .Where(p => !string.IsNullOrEmpty(p.Item1))
            .Select(p => (p.Item1!, p.Item2)).ToList();
    }

    public void Save()
    {
        DiagramDocument.AtomicWrite(Path, System.Text.Encoding.UTF8.GetBytes(Text));
    }

    private XDocument? TryParse()
    {
        try
        {
            return XDocument.Parse(Text);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private IEnumerable<string> AttributeValues(string element, string attribute) =>
        TryParse()?.Descendants().Where(e => e.Name.LocalName == element).Select(e => (string?)e.Attribute(attribute)).OfType<string>() ?? [];

    /// <summary>Ranges of <c>&lt;PropertyGroup&gt;</c> blocks without a <c>Condition</c> attribute.</summary>
    private List<TextRange> UnconditionalGroups()
    {
        var result = new List<TextRange>();
        var searchStart = 0;
        while (searchStart < Text.Length && PropertyGroupOpen().Match(Text, searchStart) is { Success: true } open)
        {
            var close = Text.IndexOf("</PropertyGroup>", open.Index + open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            var end = close + "</PropertyGroup>".Length;
            if (!open.Value.Contains("Condition", StringComparison.Ordinal))
            {
                result.Add(new TextRange(open.Index, end));
            }

            searchStart = end;
        }

        return result;
    }

    private List<(TextRange Element, TextRange Value)> PropertyMatches(string name, TextRange group)
    {
        var matches = new List<(TextRange, TextRange)>();
        var start = group.Start;
        var openTag = $"<{name}>";
        var closeTag = $"</{name}>";
        while (true)
        {
            var open = Text.IndexOf(openTag, start, group.End - start, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            var valueStart = open + openTag.Length;
            var close = Text.IndexOf(closeTag, valueStart, group.End - valueStart, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            matches.Add((new TextRange(open, close + closeTag.Length), new TextRange(valueStart, close)));
            start = close + closeTag.Length;
        }

        return matches;
    }

    [GeneratedRegex(@"<PropertyGroup(\s[^>]*)?>")]
    private static partial Regex PropertyGroupOpen();

    [GeneratedRegex("<Project[^>]*>")]
    private static partial Regex ProjectElement();

    private readonly record struct TextRange(int Start, int End)
    {
        public int Length => End - Start;
    }
}
