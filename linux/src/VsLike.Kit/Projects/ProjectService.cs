using System.Text.Json;

namespace VsLike.Kit;

public sealed record ProjectTemplate(string Name, string ShortName, string Tags)
{
    public string Id => ShortName;
}

public sealed record InstalledPackage(string Id, string? RequestedVersion, string? ResolvedVersion, string? LatestVersion)
{
    public bool HasUpdate => LatestVersion is not null && ResolvedVersion is not null && LatestVersion != ResolvedVersion;
}

/// <summary>Solutions, projects, references and packages through the dotnet CLI (<c>dotnet new</c>, <c>sln</c>, <c>add</c>, <c>remove</c>, <c>list</c>).</summary>
public sealed record ProjectService(CommandRunner Runner)
{
    public static readonly IReadOnlyList<string> PreferredTemplates = ["console", "classlib", "xunit", "nunit", "mstest", "worker", "webapi", "web", "blazor"];

    /// <summary>Exit code of <c>dotnet new</c> for an option the template does not know (aka.ms/templating-exit-codes#127).</summary>
    internal const int InvalidOptionExitCode = 127;

    internal static readonly HashSet<string> WindowsOnly = ["winforms", "winformslib", "winformscontrollib", "wpf", "wpflib", "wpfcustomcontrollib", "wpfusercontrollib"];

    public async Task<string> CreateSolution(string directory, string name, SolutionFormat format = SolutionFormat.Slnx)
    {
        Directory.CreateDirectory(directory);
        var extension = format.RawValue();
        await Runner.RunChecked(["new", "sln", "--name", name, "--output", directory, "--format", extension], directory).ConfigureAwait(false);
        return Path.Combine(directory, name + "." + extension);
    }

    public async Task<string> CreateProject(string template, string name, string directory, string? framework = null)
    {
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, name);
        var arguments = new List<string> { "new", template, "--name", name, "--output", output };
        if (!string.IsNullOrEmpty(framework))
        {
            arguments.AddRange(["--framework", framework]);
        }

        // A Program class with Main in the project's namespace instead of top-level statements. Templates without the
        // option end with exit code 127 ("invalid option") before creating anything and are created without it.
        var withMain = await Runner.Run([.. arguments, "--use-program-main"], directory).ConfigureAwait(false);
        if (withMain.ExitCode == InvalidOptionExitCode && !withMain.WasCancelled)
        {
            await Runner.RunChecked(arguments, directory).ConfigureAwait(false);
        }
        else if (!withMain.Succeeded)
        {
            throw CommandException.Failed("dotnet " + string.Join(" ", arguments), withMain.ExitCode, withMain.AllText);
        }

        var project = Path.Combine(output, name + ".csproj");
        if (!File.Exists(project))
        {
            throw CommandException.Failed($"dotnet new {template}", 0, $"No project file was created at {project}.");
        }

        UseBlockScopedNamespaces(output);
        return project;
    }

    public Task AddProject(string project, string solution, string? folder = null)
    {
        var arguments = new List<string> { "sln", solution, "add", project };
        if (folder is not null && folder != "/")
        {
            arguments.AddRange(["--solution-folder", folder.Trim('/')]);
        }

        return Runner.RunChecked(arguments, Path.GetDirectoryName(solution));
    }

    public Task RemoveProject(string project, string solution) => Runner.RunChecked(["sln", solution, "remove", project], Path.GetDirectoryName(solution));

    public Task AddReference(string reference, string project) => Runner.RunChecked(["add", project, "reference", reference]);

    public Task RemoveReference(string reference, string project) => Runner.RunChecked(["remove", project, "reference", reference]);

    public Task AddPackage(string id, string? version, string project)
    {
        var arguments = new List<string> { "add", project, "package", id };
        if (!string.IsNullOrEmpty(version))
        {
            arguments.AddRange(["--version", version]);
        }

        return Runner.RunChecked(arguments);
    }

    public Task RemovePackage(string id, string project) => Runner.RunChecked(["remove", project, "package", id]);

    public async Task<IReadOnlyList<InstalledPackage>> ListPackages(string project, bool outdated = false)
    {
        var arguments = new List<string> { "list", project, "package", "--format", "json" };
        if (outdated)
        {
            arguments.Add("--outdated");
        }

        var result = await Runner.RunChecked(arguments).ConfigureAwait(false);
        return ParsePackageList(result.StandardOutput);
    }

    public async Task<IReadOnlyList<ProjectTemplate>> Templates()
    {
        try
        {
            var result = await Runner.Run(["new", "list", "--type", "project", "--language", "C#"]).ConfigureAwait(false);
            return result.Succeeded ? ParseTemplates(result.StandardOutput) : [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    public static IReadOnlyList<InstalledPackage> ParsePackageList(string json)
    {
        var start = json.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json[start..]);
            var seen = new HashSet<string>();
            var packages = new List<InstalledPackage>();
            foreach (var package in Items(document.RootElement, "projects").SelectMany(p => Items(p, "frameworks")).SelectMany(f => Items(f, "topLevelPackages")))
            {
                if (Str(package, "id") is not { } id || !seen.Add(id))
                {
                    continue;
                }

                packages.Add(new InstalledPackage(id, Str(package, "requestedVersion"), Str(package, "resolvedVersion"), Str(package, "latestVersion")));
            }

            return packages;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Parses the table of <c>dotnet new list</c> (columns are found from the dashed ruler line).</summary>
    public static IReadOnlyList<ProjectTemplate> ParseTemplates(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim('\r')).ToList();
        var rulerIndex = lines.FindIndex(l => l.StartsWith("---", StringComparison.Ordinal) && l.Contains("  ", StringComparison.Ordinal));
        if (rulerIndex < 0)
        {
            return [];
        }

        var columns = new List<(int Start, int End)>();
        int? start = null;
        var ruler = lines[rulerIndex] + " ";
        for (var index = 0; index < ruler.Length; index++)
        {
            if (ruler[index] == '-' && start is null)
            {
                start = index;
            }
            else if (ruler[index] != '-' && start is { } begin)
            {
                columns.Add((begin, index));
                start = null;
            }
        }

        if (columns.Count < 3)
        {
            return [];
        }

        columns[^1] = (columns[^1].Start, int.MaxValue);
        var templates = new List<ProjectTemplate>();
        foreach (var line in lines.Skip(rulerIndex + 1).Where(l => l.Trim().Length > 0))
        {
            string Cell(int index)
            {
                var (from, to) = columns[index];
                return from >= line.Length ? string.Empty : line[from..Math.Min(line.Length, to)].Trim();
            }

            var shortName = Cell(1).Split(',')[0].Trim();
            if (shortName.Length == 0 || WindowsOnly.Contains(shortName.ToLowerInvariant()))
            {
                continue;
            }

            templates.Add(new ProjectTemplate(Cell(0), shortName, columns.Count > 3 ? Cell(3) : string.Empty));
        }

        static int Rank(ProjectTemplate t)
        {
            var index = PreferredTemplates.ToList().IndexOf(t.ShortName);
            return index < 0 ? int.MaxValue : index;
        }

        return [.. templates.OrderBy(Rank).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The templates write <c>namespace X;</c>; VsLike's new files use <c>namespace X { … }</c>.</summary>
    private static void UseBlockScopedNamespaces(string folder)
    {
        foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.cs") : [])
        {
            var source = File.ReadAllText(file);
            var converted = NamespaceStyle.BlockScoped(source);
            if (converted != source)
            {
                File.WriteAllText(file, converted);
            }
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
