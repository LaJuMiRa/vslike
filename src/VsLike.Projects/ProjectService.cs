using System.Text.Json;
using VsLike.Core.Build;
using VsLike.Projects.Solutions;

namespace VsLike.Projects;

/// <summary>
/// Solution- und Projektoperationen über das dotnet-CLI, damit Dateien genau so geschrieben werden wie vom SDK.
/// Jede Methode wirft <see cref="ProjectOperationException"/> mit der CLI-Ausgabe, wenn der Aufruf scheitert.
/// </summary>
public sealed class ProjectService
{
    private readonly IDotnetCli _cli;

    public ProjectService(IDotnetCli cli)
    {
        _cli = cli;
    }

    public static IReadOnlyList<InstalledPackage> ParsePackageList(string json)
    {
        var start = json.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(json[start..]);
        var packages = new List<InstalledPackage>();
        foreach (var project in Array(document.RootElement, "projects"))
        {
            foreach (var framework in Array(project, "frameworks"))
            {
                foreach (var package in Array(framework, "topLevelPackages"))
                {
                    packages.Add(new InstalledPackage(
                        package.GetProperty("id").GetString() ?? string.Empty,
                        Text(package, "requestedVersion"),
                        Text(package, "resolvedVersion"),
                        Text(package, "latestVersion")));
                }
            }
        }

        return [.. packages.DistinctBy(p => p.Id)];
    }

    /// <summary>Creates <c>directory/name.slnx</c> (or .sln) and returns its path.</summary>
    public async Task<string> CreateSolutionAsync(string directory, string name, SolutionFormat format, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await RunAsync(["new", "sln", "--name", name, "--output", directory, "--format", format == SolutionFormat.Slnx ? "slnx" : "sln"], directory, cancellationToken).ConfigureAwait(false);
        return Path.Combine(directory, name + (format == SolutionFormat.Slnx ? ".slnx" : ".sln"));
    }

    /// <summary>Creates a project from a template in <c>directory/name</c> and returns the csproj path.</summary>
    public async Task<string> CreateProjectAsync(string template, string name, string directory, string? framework, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, name);
        List<string> args = ["new", template, "--name", name, "--output", output];
        if (!string.IsNullOrEmpty(framework))
        {
            args.AddRange(["--framework", framework]);
        }

        await RunAsync(args, directory, cancellationToken).ConfigureAwait(false);
        var project = Path.Combine(output, name + ".csproj");
        if (!File.Exists(project))
        {
            throw new ProjectOperationException($"Template '{template}' did not create {project}.", string.Empty);
        }

        return project;
    }

    public Task AddProjectToSolutionAsync(string solution, string project, string? solutionFolder, CancellationToken cancellationToken)
    {
        List<string> args = ["sln", solution, "add", project];
        if (!string.IsNullOrEmpty(solutionFolder) && solutionFolder != "/")
        {
            args.AddRange(["--solution-folder", solutionFolder.Trim('/')]);
        }

        return RunAsync(args, Path.GetDirectoryName(solution), cancellationToken);
    }

    public Task RemoveProjectFromSolutionAsync(string solution, string project, CancellationToken cancellationToken)
        => RunAsync(["sln", solution, "remove", project], Path.GetDirectoryName(solution), cancellationToken);

    public Task AddProjectReferenceAsync(string project, string reference, CancellationToken cancellationToken)
        => RunAsync(["add", project, "reference", reference], Path.GetDirectoryName(project), cancellationToken);

    public Task RemoveProjectReferenceAsync(string project, string reference, CancellationToken cancellationToken)
        => RunAsync(["remove", project, "reference", reference], Path.GetDirectoryName(project), cancellationToken);

    public Task AddPackageAsync(string project, string packageId, string? version, CancellationToken cancellationToken)
    {
        List<string> args = ["add", project, "package", packageId];
        if (!string.IsNullOrEmpty(version))
        {
            args.AddRange(["--version", version]);
        }

        return RunAsync(args, Path.GetDirectoryName(project), cancellationToken);
    }

    public Task RemovePackageAsync(string project, string packageId, CancellationToken cancellationToken)
        => RunAsync(["remove", project, "package", packageId], Path.GetDirectoryName(project), cancellationToken);

    /// <summary>Lists top-level packages (requested and resolved versions; latest when <paramref name="outdated"/>).</summary>
    public async Task<IReadOnlyList<InstalledPackage>> ListPackagesAsync(string project, bool outdated, CancellationToken cancellationToken)
    {
        List<string> args = ["list", project, "package", "--format", "json"];
        if (outdated)
        {
            args.Add("--outdated");
        }

        var result = await RunAsync(args, Path.GetDirectoryName(project), cancellationToken).ConfigureAwait(false);
        return ParsePackageList(result.StandardOutput);
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> args, string? workingDirectory, CancellationToken cancellationToken)
    {
        var result = await _cli.RunAsync(args, workingDirectory, null, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var output = string.Join('\n', result.Output.Select(l => l.Text));
            throw new ProjectOperationException($"dotnet {string.Join(' ', args)} failed (exit {result.ExitCode}).", output);
        }

        return result;
    }
}
