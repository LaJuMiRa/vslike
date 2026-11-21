using System.Diagnostics;
using VsLike.Core.Build;
using VsLike.Core.Files;

namespace VsLike.Roslyn.Tests;

/// <summary>Kopiert die Test-Solution in ein Temp-Verzeichnis, führt dotnet restore aus und lädt sie einmal.</summary>
public sealed class SampleSolutionFixture : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-roslyn", Guid.NewGuid().ToString("N"));

    public string SolutionPath => Path.Combine(Root, "Sample.slnx");

    public RoslynWorkspace Workspace { get; } = new();

    public TimeSpan LoadDuration { get; private set; }

    /// <summary>Gets or sets a hook that changes the copied files before restore and load (e.g. adds .editorconfig).</summary>
    public Action<string>? BeforeLoad { get; set; }

    public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public async Task InitializeAsync()
    {
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "TestAssets", "ThreeProjects"), Root);
        BeforeLoad?.Invoke(Root);
        await RunDotnetAsync("restore", SolutionPath);
        var result = await Workspace.LoadAsync(SolutionPath, CancellationToken.None);
        LoadDuration = result.Duration;
    }

    public Task DisposeAsync()
    {
        Workspace.Dispose();
        Directory.Delete(Root, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task RunDotnetAsync(params string[] args)
    {
        // Like the IDE: without the MSBuild variables of this test host, otherwise a newer SDK chosen for the temp folder
        // starts its task host with the SDK registered here (MSB4216).
        var start = new DotnetCli().CreateStartInfo(args, workingDirectory: null);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {string.Join(' ', args)} failed: {output}");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
