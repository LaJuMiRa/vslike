using System.Text.Json;

namespace VsLike.Projects.Build;

/// <summary>Liest die Startprofile eines Projekts; fehlende oder defekte Dateien ergeben eine leere Liste.</summary>
public static class LaunchSettingsReader
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string PathFor(string projectPath) => Path.Combine(Path.GetDirectoryName(projectPath)!, "Properties", "launchSettings.json");

    public static IReadOnlyList<LaunchProfile> Read(string projectPath)
    {
        var path = PathFor(projectPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return [];
        }
    }

    public static IReadOnlyList<LaunchProfile> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, Options);
        if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new List<LaunchProfile>();
        foreach (var profile in profiles.EnumerateObject())
        {
            var value = profile.Value;
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            if (value.TryGetProperty("environmentVariables", out var variables) && variables.ValueKind == JsonValueKind.Object)
            {
                foreach (var variable in variables.EnumerateObject())
                {
                    environment[variable.Name] = variable.Value.ToString();
                }
            }

            result.Add(new LaunchProfile(
                profile.Name,
                String(value, "commandName") ?? "Project",
                String(value, "commandLineArgs"),
                String(value, "workingDirectory"),
                String(value, "applicationUrl"),
                environment));
        }

        return result;
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
