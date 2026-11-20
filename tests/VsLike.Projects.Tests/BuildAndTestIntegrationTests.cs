using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Files;
using VsLike.Projects.Build;
using VsLike.Projects.Cli;
using VsLike.Projects.Testing;

namespace VsLike.Projects.Tests;

/// <summary>Echte dotnet-Aufrufe: Build mit Fehler, Start mit Startprofil, Abbruch, Testlauf.</summary>
public sealed class BuildAndTestIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-p6", Guid.NewGuid().ToString("N"));
    private readonly DotnetCli _cli = new();

    [Fact]
    public async Task Build_ReportsErrorWithLine_ThenRunsWithLaunchProfile()
    {
        var project = WriteConsoleApp("Con", "Console.WriteLine(\"Hi\")\nint x = 1;\n");
        var service = new BuildService(_cli);

        var failed = await service.BuildAsync(project, "Debug", BuildKind.Build, null, CancellationToken.None);
        Assert.False(failed.Succeeded);
        var error = Assert.Single(failed.Diagnostics, d => d.Severity == DiagnosticSeverityKind.Error);
        Assert.Equal("CS1002", error.Id);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(project)!, "Program.cs"), error.FilePath);
        Assert.Equal(1, error.StartLine);

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "Program.cs"), "Console.WriteLine($\"Hi {Environment.GetEnvironmentVariable(\"GREETING\")} {string.Join(',', args)}\");\n");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project)!, "Properties"));
        File.WriteAllText(LaunchSettingsReader.PathFor(project), """{ "profiles": { "Dev": { "commandName": "Project", "commandLineArgs": "a b", "environmentVariables": { "GREETING": "there" } } } }""");
        var built = await service.BuildAsync(project, "Release", BuildKind.Build, null, CancellationToken.None);
        Assert.True(built.Succeeded, string.Join('\n', built.Process.Output.Select(l => l.Text)));

        var live = new List<string>();
        var run = await service.RunAsync(project, "Release", LaunchSettingsReader.Read(project)[0], l => live.Add(l.Text), CancellationToken.None);
        Assert.True(run.Succeeded, string.Join('\n', run.Output.Select(l => l.Text)));
        Assert.Contains("Hi there a,b", live);
    }

    [Fact]
    public async Task Run_CanBeCancelled()
    {
        var project = WriteConsoleApp("Wait", "Console.WriteLine(\"started\");\nThread.Sleep(60000);\n");
        var service = new BuildService(_cli);
        Assert.True((await service.BuildAsync(project, "Debug", BuildKind.Build, null, CancellationToken.None)).Succeeded);

        using var cancellation = new CancellationTokenSource();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void OnOutput(OutputLine line)
        {
            if (line.Text == "started")
            {
                cancellation.Cancel();
            }
        }

        var run = service.RunAsync(project, "Debug", null, OnOutput, cancellation.Token);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.WasCancelled);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Tests_AreDiscoveredAndRunWithResults()
    {
        var directory = Path.Combine(_root, "Tst");
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Tst.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsPackable>false</IsPackable></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
              </ItemGroup>
              <ItemGroup><Using Include="Xunit" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "UnitTest1.cs"), """
            namespace Tst.Inner;

            public class UnitTest1
            {
                [Fact]
                public void Passes() => Assert.True(true);

                [Fact]
                public void Fails() => Assert.Equal(1, 2);

                [Theory]
                [InlineData(1)]
                [InlineData(2)]
                public void Data(int x) => Assert.True(x > 0);
            }
            """);
        Assert.True(TestService.IsTestProject(project));
        var service = new TestService(_cli);

        var (tests, discovery) = await service.DiscoverAsync(project, "Debug", null, CancellationToken.None);
        Assert.True(tests.Count == 4, string.Join('\n', discovery.Output.Select(l => l.Text)));

        var (all, _) = await service.RunAsync(project, "Debug", null, null, CancellationToken.None);
        Assert.Equal(4, all.Count);
        var failed = Assert.Single(all, r => r.Outcome == TestOutcome.Failed);
        Assert.Equal("Tst.Inner.UnitTest1.Fails", failed.Name);
        Assert.Equal((Path.Combine(directory, "UnitTest1.cs"), 9), TestOutputParser.FirstSourceFrame(failed.StackTrace));

        var (data, _) = await service.RunAsync(project, "Debug", TestService.BuildFilter(["Tst.Inner.UnitTest1.Data(x: 1)"], []), null, CancellationToken.None);
        Assert.Equal(2, data.Count);
        Assert.All(data, r => Assert.Equal(TestOutcome.Passed, r.Outcome));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteConsoleApp(string name, string program)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(directory, "Program.cs"), program);
        return project;
    }
}
