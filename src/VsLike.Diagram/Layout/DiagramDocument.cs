using System.Text.Json;

namespace VsLike.Diagram.Layout;

/// <summary>
/// Inhalt einer <c>&lt;Name&gt;.cdiagram.json</c>: welche Typen im Diagramm stehen und wo. Der Code bleibt die einzige
/// Quelle für Typen und Mitglieder; diese Datei speichert nur Layout und Ansicht.
/// </summary>
public sealed record DiagramDocument
{
    public const string Extension = ".cdiagram.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public int Version { get; init; } = 1;

    public IReadOnlyList<TypePlacement> Types { get; init; } = [];

    public double Zoom { get; init; } = 1;

    public double OffsetX { get; init; }

    public double OffsetY { get; init; }

    public bool ShowGrid { get; init; } = true;

    public bool SnapToGrid { get; init; } = true;

    public static DiagramDocument Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<DiagramDocument>(File.ReadAllText(path), Options) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static string NameOf(string path) => Path.GetFileName(path)[..^Extension.Length];

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";

    public TypePlacement? Find(string fullName) => Types.FirstOrDefault(t => t.FullName == fullName);

    public DiagramDocument WithPlacement(TypePlacement placement)
        => this with { Types = [.. Types.Where(t => t.FullName != placement.FullName), placement] };

    public DiagramDocument Without(string fullName) => this with { Types = [.. Types.Where(t => t.FullName != fullName)] };

    /// <summary>Follows a rename so the type keeps its position.</summary>
    public DiagramDocument Renamed(string oldFullName, string newFullName)
        => this with { Types = [.. Types.Select(t => t.FullName == oldFullName ? t with { FullName = newFullName } : t)] };
}
