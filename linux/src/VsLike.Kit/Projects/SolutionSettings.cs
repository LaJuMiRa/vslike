using System.Text.Json;
using System.Text.Json.Serialization;

namespace VsLike.Kit;

/// <summary>Per-solution settings in <c>&lt;solution folder&gt;/.vslike/settings.json</c>. Written only when the user changes something.</summary>
public sealed record SolutionSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Properties in alphabetical order: the Swift original writes sorted keys.
    public IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>>? Breakpoints { get; init; }

    public string? Configuration { get; init; }

    public string? LaunchProfile { get; init; }

    public string? StartupProject { get; init; }

    /// <summary>Gets null for "use the global default".</summary>
    public bool? StyleCopEnabled { get; init; }

    public static string File(string solution) => Path.Combine(Path.GetDirectoryName(solution) ?? "/", ".vslike/settings.json");

    public static SolutionSettings Load(string solution)
    {
        try
        {
            return JsonSerializer.Deserialize<SolutionSettings>(System.IO.File.ReadAllBytes(File(solution)), Options) ?? new SolutionSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SolutionSettings();
        }
    }

    public bool Equals(SolutionSettings? other) =>
        other is not null && StyleCopEnabled == other.StyleCopEnabled && StartupProject == other.StartupProject
        && Configuration == other.Configuration && LaunchProfile == other.LaunchProfile && SameBreakpoints(Breakpoints, other.Breakpoints);

    public override int GetHashCode() => HashCode.Combine(StyleCopEnabled, StartupProject, Configuration, LaunchProfile, Breakpoints?.Count);

    public void Save(string solution)
    {
        var path = File(solution);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DiagramDocument.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(this, Options));
    }

    private static bool SameBreakpoints(IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>>? left, IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count && left.All(e => right.TryGetValue(e.Key, out var list) && list.SequenceEqual(e.Value));
    }
}
