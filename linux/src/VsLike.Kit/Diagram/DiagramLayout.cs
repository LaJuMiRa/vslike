using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VsLike.Kit;

/// <summary>Position of one type on a diagram. <see cref="X"/>/<see cref="Y"/> are NaN for a type that still has to be placed by <see cref="AutoLayout"/>.</summary>
public sealed record TypePlacement
{
    [JsonConstructor]
    public TypePlacement(string fullName, double x = double.NaN, double y = double.NaN, bool collapsed = false)
    {
        FullName = fullName;
        X = x;
        Y = y;
        Collapsed = collapsed;
    }

    [JsonPropertyOrder(1)]
    public string FullName { get; init; }

    [JsonPropertyOrder(2)]
    public double X { get; init; }

    [JsonPropertyOrder(3)]
    public double Y { get; init; }

    [JsonPropertyOrder(0)]
    public bool Collapsed { get; init; }

    [JsonIgnore]
    public bool IsPlaced => !double.IsNaN(X) && !double.IsNaN(Y);

    public TypePlacement Moved(double x, double y) => this with { X = x, Y = y };

    public TypePlacement Toggled() => this with { Collapsed = !Collapsed };
}

/// <summary>
/// The layout file <c>&lt;Name&gt;.cdiagram.json</c> (or a <c>.cd</c> class diagram, see <see cref="ClassDiagramFile"/>): which types
/// are shown where, plus view settings. Types and members always come from the code; this file holds no code information.
/// </summary>
public sealed record DiagramDocument
{
    public const string FileExtension = ".cdiagram.json";

    private static readonly JsonSerializerOptions WriteOptions = new(JSONValue.Options) { WriteIndented = true, IndentSize = 2 };

    public DiagramDocument()
        : this([])
    {
    }

    public DiagramDocument(IReadOnlyList<TypePlacement> types)
    {
        Types = types;
    }

    // Properties in alphabetical order: the Swift original writes sorted keys.
    public double OffsetX { get; init; }

    public double OffsetY { get; init; }

    public bool ShowGrid { get; init; } = true;

    public bool SnapToGrid { get; init; } = true;

    public IReadOnlyList<TypePlacement> Types { get; init; }

    public int Version { get; init; } = 1;

    public double Zoom { get; init; } = 1;

    /// <summary>Whether the file is a diagram VsLike opens on its canvas (<c>.cdiagram.json</c> or <c>.cd</c>).</summary>
    public static bool IsDiagram(string path) => path.EndsWith(FileExtension, StringComparison.Ordinal) || ClassDiagramFile.IsClassDiagram(path);

    public static string Name(string path)
    {
        var file = Path.GetFileName(path);
        if (ClassDiagramFile.IsClassDiagram(path))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        return file.EndsWith(FileExtension, StringComparison.Ordinal) ? file[..^FileExtension.Length] : file;
    }

    public static DiagramDocument Load(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (ClassDiagramFile.IsClassDiagram(path))
            {
                return ClassDiagramFile.Read(data);
            }

            return JsonSerializer.Deserialize<DiagramDocument>(data, JSONValue.Options) ?? new DiagramDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
        {
            return new DiagramDocument();
        }
    }

    public byte[] JsonData()
    {
        var text = JsonSerializer.Serialize(this, WriteOptions) + "\n";
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Saves as JSON, or into the <c>.cd</c> file (<paramref name="types"/> describes types that are new on the diagram).</summary>
    public void Save(string path, IReadOnlyDictionary<string, ClassDiagramFile.TypeInfo>? types = null)
    {
        byte[] data;
        if (ClassDiagramFile.IsClassDiagram(path))
        {
            byte[]? existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
            data = ClassDiagramFile.Write(this, existing, types ?? new Dictionary<string, ClassDiagramFile.TypeInfo>());
        }
        else
        {
            data = JsonData();
        }

        AtomicWrite(path, data);
    }

    public TypePlacement? Find(string fullName) => Types.FirstOrDefault(t => t.FullName == fullName);

    public DiagramDocument With(TypePlacement placement)
    {
        var list = Types.ToList();
        var index = list.FindIndex(t => t.FullName == placement.FullName);
        if (index >= 0)
        {
            list[index] = placement;
        }
        else
        {
            list.Add(placement);
        }

        return this with { Types = list };
    }

    public DiagramDocument Without(string fullName) => this with { Types = [.. Types.Where(t => t.FullName != fullName)] };

    public DiagramDocument Renamed(string oldName, string newName) =>
        this with { Types = [.. Types.Select(t => t.FullName == oldName ? t with { FullName = newName } : t)] };

    public bool Equals(DiagramDocument? other) =>
        other is not null && Version == other.Version && Zoom.Equals(other.Zoom) && OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY)
        && ShowGrid == other.ShowGrid && SnapToGrid == other.SnapToGrid && Types.SequenceEqual(other.Types);

    public override int GetHashCode() => HashCode.Combine(Version, Zoom, Types.Count);

    internal static void AtomicWrite(string path, byte[] data)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// Box sizes in diagram coordinates. Member lines use a monospaced font, so sizes depend only on character counts and
/// are identical on the canvas, in hit testing and in the exports.
/// </summary>
public static class BoxMetrics
{
    public const double FontSize = 12.0;
    public const double CharWidth = FontSize * 0.6;
    public const double LineHeight = 18.0;
    public const double HeaderHeight = 44.0;
    public const double Padding = 10.0;
    public const double MinWidth = 160.0;
    public const double GridSize = 20.0;

    public static IReadOnlyList<string> Lines(DiagramType type, bool collapsed) =>
        collapsed || type.IsExternal ? [] : [.. type.Members.Select(m => m.UmlLine)];

    public static CGSize Size(DiagramType type, bool collapsed)
    {
        var lines = Lines(type, collapsed);
        var longest = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
        var header = Math.Max(type.DisplayName.Length * CharWidth * 1.2, type.Stereotype.Length * CharWidth);
        var width = Math.Max(MinWidth, Math.Max(header, longest * CharWidth) + (2 * Padding) + 8);
        var height = HeaderHeight + (lines.Count == 0 ? 0 : (lines.Count * LineHeight) + Padding);
        return new CGSize(Snap(width, up: true), height);
    }

    public static double Snap(double value, bool up = false) =>
        up ? Math.Ceiling(value / GridSize) * GridSize : Math.Round(value / GridSize, MidpointRounding.AwayFromZero) * GridSize;
}

/// <summary>A type box on the canvas.</summary>
public sealed record DiagramBox(DiagramType Type, CGRect Frame, bool Collapsed)
{
    public CGPoint Center => new(Frame.MidX, Frame.MidY);

    /// <summary>Point where the line from the center towards <paramref name="point"/> leaves the box.</summary>
    public CGPoint Border(CGPoint point)
    {
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return Center;
        }

        var sx = Math.Abs(dx) < 0.001 ? double.MaxValue : Frame.Width / 2 / Math.Abs(dx);
        var sy = Math.Abs(dy) < 0.001 ? double.MaxValue : Frame.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(sx, sy);
        return new CGPoint(Center.X + (dx * scale), Center.Y + (dy * scale));
    }

    /// <summary>Index of the member line at a point inside the box, or null for the header.</summary>
    public int? MemberIndex(CGPoint point)
    {
        var offset = point.Y - Frame.MinY - BoxMetrics.HeaderHeight;
        if (Collapsed || offset < 0)
        {
            return null;
        }

        var index = (int)(offset / BoxMetrics.LineHeight);
        return index < Type.Members.Count ? index : null;
    }
}

public sealed record DiagramEdge(DiagramRelation Relation, CGPoint Start, CGPoint End);

public static class DiagramGeometry
{
    public static IReadOnlyList<DiagramBox> Boxes(DiagramModel model, DiagramDocument document)
    {
        var byName = new Dictionary<string, DiagramType>();
        foreach (var type in model.Types)
        {
            byName.TryAdd(type.FullName, type);
        }

        return document.Types
            .Where(p => p.IsPlaced && byName.ContainsKey(p.FullName))
            .Select(p =>
            {
                var type = byName[p.FullName];
                var size = BoxMetrics.Size(type, p.Collapsed);
                return new DiagramBox(type, new CGRect(p.X, p.Y, size.Width, size.Height), p.Collapsed);
            }).ToList();
    }

    /// <summary>Relations whose both ends are on the diagram, as border-to-border segments.</summary>
    public static IReadOnlyList<DiagramEdge> Edges(DiagramModel model, IReadOnlyList<DiagramBox> boxes)
    {
        var byName = new Dictionary<string, DiagramBox>();
        foreach (var box in boxes)
        {
            byName.TryAdd(box.Type.FullName, box);
        }

        return model.Relations
            .Where(r => r.From != r.To && byName.ContainsKey(r.From) && byName.ContainsKey(r.To))
            .Select(r =>
            {
                var from = byName[r.From];
                var to = byName[r.To];
                return new DiagramEdge(r, from.Border(to.Center), to.Border(from.Center));
            }).ToList();
    }

    public static CGRect Bounds(IReadOnlyList<DiagramBox> boxes, double margin = 20)
    {
        if (boxes.Count == 0)
        {
            return CGRect.Zero;
        }

        return boxes.Skip(1).Aggregate(boxes[0].Frame, (rect, box) => rect.Union(box.Frame)).InsetBy(-margin, -margin);
    }
}

/// <summary>
/// Layered layout: base types and interfaces above the types that derive from them. Existing positions stay; only
/// unplaced types are arranged (or all of them with <c>all</c>).
/// </summary>
public static class AutoLayout
{
    internal const double HorizontalGap = 60.0;
    internal const double VerticalGap = 80.0;

    public static DiagramDocument Arrange(DiagramDocument document, DiagramModel model, bool all)
    {
        var shown = document.Types.Select(t => t.FullName).ToHashSet();
        var types = model.Types.Where(t => shown.Contains(t.FullName)).ToList();
        var toPlace = all ? types : [.. types.Where(t => document.Find(t.FullName)?.IsPlaced != true)];
        if (toPlace.Count == 0)
        {
            return document;
        }

        var parents = new Dictionary<string, List<string>>();
        foreach (var relation in model.Relations.Where(r => r.Kind is RelationKind.Inheritance or RelationKind.Implementation && shown.Contains(r.From) && shown.Contains(r.To)))
        {
            if (!parents.TryGetValue(relation.From, out var list))
            {
                parents[relation.From] = list = [];
            }

            list.Add(relation.To);
        }

        var depthCache = new Dictionary<string, int>();
        int Depth(string name, int level)
        {
            if (depthCache.TryGetValue(name, out var known))
            {
                return known;
            }

            var value = level > 32 ? 0 : parents.GetValueOrDefault(name)?.Select(p => Depth(p, level + 1) + 1).DefaultIfEmpty(0).Max() ?? 0;
            depthCache[name] = value;
            return value;
        }

        var placing = toPlace.Select(t => t.FullName).ToHashSet();
        var placed = all ? [] : document.Types.Where(t => t.IsPlaced && !placing.Contains(t.FullName)).ToList();
        var y = placed.Count == 0
            ? BoxMetrics.GridSize
            : placed.Max(p => p.Y + BoxMetrics.Size(model.Find(p.FullName) ?? new DiagramType(p.FullName, p.FullName), p.Collapsed).Height) + VerticalGap;
        var result = document;
        foreach (var layer in toPlace.GroupBy(t => Depth(t.FullName, 0)).OrderBy(g => g.Key))
        {
            var x = BoxMetrics.GridSize;
            var rowHeight = 0.0;
            foreach (var type in layer.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var collapsed = document.Find(type.FullName)?.Collapsed ?? false;
                var size = BoxMetrics.Size(type, collapsed);
                result = result.With(new TypePlacement(type.FullName, BoxMetrics.Snap(x), BoxMetrics.Snap(y), collapsed));
                x += size.Width + HorizontalGap;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            y += rowHeight + VerticalGap;
        }

        return result;
    }
}
