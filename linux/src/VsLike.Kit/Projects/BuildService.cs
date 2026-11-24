using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VsLike.Kit;

public enum BuildKind
{
    Build,
    Rebuild,
    Clean,
}

public sealed record BuildResult(ProcessResult Process, IReadOnlyList<Diagnostic> Diagnostics, TimeSpan Duration)
{
    public bool Succeeded => Process.Succeeded;

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
}

/// <summary>A launch profile from <c>Properties/launchSettings.json</c>.</summary>
public sealed partial record LaunchProfile(
    string Name,
    string CommandName,
    string? CommandLineArgs,
    string? WorkingDirectory,
    string? ApplicationURL,
    IReadOnlyDictionary<string, string> Environment)
{
    public string Id => Name;

    /// <summary>Gets a value indicating whether <c>dotnet run</c> can start the profile ("Project" and "Executable", not IIS or Docker ones).</summary>
    public bool IsRunnable => CommandName.ToLowerInvariant() is "project" or "executable";

    public static string File(string project) => Path.Combine(Path.GetDirectoryName(project) ?? "/", "Properties/launchSettings.json");

    /// <summary>Profiles of a project; a missing or broken file gives an empty list.</summary>
    public static IReadOnlyList<LaunchProfile> Read(string project)
    {
        try
        {
            return Parse(System.IO.File.ReadAllBytes(File(project)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<LaunchProfile> Parse(byte[] data)
    {
        // launchSettings.json may contain comments and trailing commas.
        var text = System.Text.Encoding.UTF8.GetString(data);
        var cleaned = TrailingComma().Replace(LineComment().Replace(text, string.Empty), "$1");
        try
        {
            using var document = JsonDocument.Parse(cleaned);
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return profiles.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => FromJson(p.Name, p.Value)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public bool Equals(LaunchProfile? other) =>
        other is not null && Name == other.Name && CommandName == other.CommandName && CommandLineArgs == other.CommandLineArgs
        && WorkingDirectory == other.WorkingDirectory && ApplicationURL == other.ApplicationURL
        && Environment.Count == other.Environment.Count && Environment.All(e => other.Environment.TryGetValue(e.Key, out var v) && v == e.Value);

    public override int GetHashCode() => HashCode.Combine(Name, CommandName);

    private static LaunchProfile FromJson(string name, JsonElement value)
    {
        var environment = new Dictionary<string, string>();
        if (value.TryGetProperty("environmentVariables", out var variables) && variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                environment[variable.Name] = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString() ?? string.Empty : variable.Value.GetRawText();
            }
        }

        return new LaunchProfile(
            name,
            StringProperty(value, "commandName") ?? "Project",
            StringProperty(value, "commandLineArgs"),
            StringProperty(value, "workingDirectory"),
            StringProperty(value, "applicationUrl"),
            environment);
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"(?m)^\s*//.*$")]
    private static partial Regex LineComment();

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingComma();
}

/// <summary>Build, rebuild, clean and the command line for starting a project, all through the dotnet CLI.</summary>
public sealed record BuildService(CommandRunner Runner)
{
    public static IReadOnlyList<string> BuildArguments(string target, string configuration, BuildKind kind)
    {
        var arguments = new List<string> { kind == BuildKind.Clean ? "clean" : "build", target, "-c", configuration, "-nologo", "-v:minimal", "-tl:off" };
        if (kind == BuildKind.Rebuild)
        {
            arguments.Add("--no-incremental");
        }

        return arguments;
    }

    /// <summary>Arguments for <c>dotnet run</c> of an already built project with its launch profile (or none).</summary>
    public static IReadOnlyList<string> RunArguments(string project, string configuration, LaunchProfile? profile)
    {
        var arguments = new List<string> { "run", "--project", project, "-c", configuration, "--no-build" };
        if (profile is not null)
        {
            arguments.AddRange(["--launch-profile", profile.Name]);
        }
        else
        {
            arguments.Add("--no-launch-profile");
        }

        return arguments;
    }

    public static string WorkingDirectory(string project, LaunchProfile? profile)
    {
        var projectDirectory = Path.GetDirectoryName(project) ?? "/";
        if (string.IsNullOrEmpty(profile?.WorkingDirectory))
        {
            return projectDirectory;
        }

        var expanded = Regex.Replace(profile.WorkingDirectory, Regex.Escape("$(ProjectDir)"), (projectDirectory + "/").Replace("$", "$$", StringComparison.Ordinal), RegexOptions.IgnoreCase);
        var full = Path.GetFullPath(Path.Combine(projectDirectory, expanded));
        return full.Length > 1 ? full.TrimEnd('/') : full;
    }

    public async Task<BuildResult> Build(string target, string configuration, BuildKind kind, Action<OutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = await Runner.Run(BuildArguments(target, configuration, kind), Path.GetDirectoryName(target), onOutput: onOutput, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new BuildResult(process, BuildOutputParser.Parse(process.Output), stopwatch.Elapsed);
    }

    /// <summary>The built assembly of a project (<c>TargetPath</c>), used by the debugger.</summary>
    public async Task<string?> TargetPath(string project, string configuration)
    {
        var result = await Runner.Run(["msbuild", project, "-getProperty:TargetPath", $"-p:Configuration={configuration}", "-nologo"]).ConfigureAwait(false);
        var path = result.StandardOutput.Trim();
        return result.Succeeded && path.Length > 0 ? path : null;
    }
}
