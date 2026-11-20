using System.Diagnostics;
using VsLike.Core.Build;

namespace VsLike.Projects.Build;

/// <summary>Build, Rebuild, Clean und Start über die dotnet-CLI mit Live-Ausgabe und Abbruch.</summary>
public sealed class BuildService
{
    private readonly IDotnetCli _cli;

    public BuildService(IDotnetCli cli)
    {
        _cli = cli;
    }

    public static IReadOnlyList<string> BuildArguments(string target, string configuration, BuildKind kind)
    {
        var verb = kind == BuildKind.Clean ? "clean" : "build";
        var arguments = new List<string> { verb, target, "-c", configuration, "-nologo", "-v:minimal", "-tl:off" };
        if (kind == BuildKind.Rebuild)
        {
            arguments.Add("--no-incremental");
        }

        return arguments;
    }

    /// <summary>Arguments for starting an already built project with its launch profile (or none).</summary>
    public static IReadOnlyList<string> RunArguments(string project, string configuration, LaunchProfile? profile)
    {
        var arguments = new List<string> { "run", "--project", project, "-c", configuration, "--no-build" };
        if (profile is null)
        {
            arguments.Add("--no-launch-profile");
        }
        else
        {
            arguments.AddRange(["--launch-profile", profile.Name]);
        }

        return arguments;
    }

    public async Task<BuildResult> BuildAsync(string target, string configuration, BuildKind kind, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var process = await _cli.RunAsync(BuildArguments(target, configuration, kind), Path.GetDirectoryName(target), onOutput, cancellationToken).ConfigureAwait(false);
        return new BuildResult(process, BuildOutputParser.Parse(process.Output), watch.Elapsed);
    }

    public Task<ProcessResult> RunAsync(string project, string configuration, LaunchProfile? profile, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var workingDirectory = profile?.WorkingDirectory is { Length: > 0 } directory
            ? Path.GetFullPath(directory.Replace("$(ProjectDir)", Path.GetDirectoryName(project) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Path.GetDirectoryName(project)!)
            : Path.GetDirectoryName(project);
        return _cli.RunAsync(RunArguments(project, configuration, profile), workingDirectory, onOutput, cancellationToken);
    }
}
