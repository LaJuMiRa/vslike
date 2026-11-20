using VsLike.Core.Build;
using VsLike.Core.Files;
using VsLike.Projects.Cli;
using VsLike.Projects.ProjectFiles;
using VsLike.Projects.Solutions;

namespace VsLike.Projects.Tests;

/// <summary>Abnahme Phase 5: neue Solution mit 2 Projekten und Referenz baut außerhalb der IDE fehlerfrei.</summary>
public sealed class ProjectServiceIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-p5", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NewSolutionWithTwoProjectsAndReference_BuildsWithDotnet()
    {
        var cli = new DotnetCli();
        var service = new ProjectService(cli);

        var solution = await service.CreateSolutionAsync(_root, "Shop", SolutionFormat.Slnx, CancellationToken.None);
        var app = await service.CreateProjectAsync("console", "Shop.App", _root, "net10.0", CancellationToken.None);
        var lib = await service.CreateProjectAsync("classlib", "Shop.Core", _root, "net10.0", CancellationToken.None);
        await service.AddProjectToSolutionAsync(solution, app, null, CancellationToken.None);
        await service.AddProjectToSolutionAsync(solution, lib, "/libs/", CancellationToken.None);
        await service.AddProjectReferenceAsync(app, lib, CancellationToken.None);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(lib)!, "Class1.cs"), "namespace Shop.Core;\n\npublic static class Greeter\n{\n    public static string Hello() => \"Hello\";\n}\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(app)!, "Program.cs"), "Console.WriteLine(Shop.Core.Greeter.Hello());\n");

        var model = SolutionReader.Read(solution);
        Assert.Equal(["Shop.App", "Shop.Core"], model.Projects.Select(p => p.Name).Order());
        Assert.Equal("/libs/", model.Projects.Single(p => p.Name == "Shop.Core").SolutionFolder);
        Assert.Equal([lib], ProjectFile.Load(app).ProjectReferences());
        Assert.Equal("Shop.App", ProjectFile.Load(app).GetProperty("RootNamespace") ?? Path.GetFileNameWithoutExtension(app));

        var build = await cli.RunAsync(["build", solution], _root, null, CancellationToken.None);
        Assert.True(build.Succeeded, string.Join('\n', build.Output.Select(l => l.Text)));
        var run = await cli.RunAsync(["run", "--project", app, "--no-build"], _root, null, CancellationToken.None);
        Assert.Contains(run.Output, l => l.Text == "Hello");
    }

    [Fact]
    public async Task FailingCommand_ThrowsWithCliOutput()
    {
        var service = new ProjectService(new DotnetCli());

        var error = await Assert.ThrowsAsync<ProjectOperationException>(() => service.CreateProjectAsync("does-not-exist-template", "X", _root, null, CancellationToken.None));

        Assert.NotEmpty(error.Output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
