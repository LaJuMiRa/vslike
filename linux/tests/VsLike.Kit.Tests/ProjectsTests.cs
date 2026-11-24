using System.Text;
using VsLike.Kit;
using Xunit;

namespace VsLike.Kit.Tests;

public sealed class ProjectsTests
{
    [Fact]
    public void BuildOutput_ParsesCanonicalErrorWithProject()
    {
        var diagnostic = Assert.IsType<Diagnostic>(BuildOutputParser.Parse("/src/App/Program.cs(12,5): error CS1002: ; expected [/src/App/App.csproj]"));

        Assert.Equal("CS1002", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("/src/App/Program.cs", diagnostic.FilePath);
        Assert.Equal((12, 5), (diagnostic.StartLine, diagnostic.StartColumn));
        Assert.Equal("; expected", diagnostic.Message);
        Assert.Equal("App", diagnostic.ProjectName);
    }

    [Fact]
    public void BuildOutput_ResolvesRelativeOriginAgainstProjectAndDeduplicates()
    {
        OutputLine[] lines =
        [
            new("Models/Car.cs(3,1): warning CS8618: Non-nullable property [/src/Lib/Lib.csproj]"),
            new("Build succeeded."),
            new("Models/Car.cs(3,1): warning CS8618: Non-nullable property [/src/Lib/Lib.csproj]"),
            new("/src/Lib/Lib.csproj : warning NU1603: Package resolved to a higher version"),
        ];

        var diagnostics = BuildOutputParser.Parse(lines);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal("/src/Lib/Models/Car.cs", diagnostics[0].FilePath);
        Assert.Equal("NU1603", diagnostics[1].Code);
        Assert.Equal(1, diagnostics[1].StartLine);
    }

    [Fact]
    public void Slnx_ReadsProjectsAndFolders()
    {
        const string text = """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/App/App.csproj" />
              </Folder>
              <Project Path="Lib\Lib.csproj" />
            </Solution>
            """;

        var model = SolutionReader.ParseSlnx("/work/Demo.slnx", text);

        Assert.Equal("Demo", model.Name);
        Assert.Equal(["App", "Lib"], model.Projects.Select(p => p.Name));
        Assert.Equal("/work/src/App/App.csproj", model.Projects[0].FilePath);
        Assert.Equal("/src/", model.Projects[0].SolutionFolder);
        Assert.Equal("/work/Lib/Lib.csproj", model.Projects[1].FilePath);
    }

    [Fact]
    public void Sln_ReadsNestedSolutionFolders()
    {
        var model = SolutionReader.ParseSln("/work/Demo.sln", File.ReadAllText(Repository.Fixture("sample.sln")));

        Assert.Equal(["App", "Tests"], model.Projects.Select(p => p.Name));
        Assert.Equal("/src/", model.Projects[0].SolutionFolder);
        Assert.Equal("/work/src/App/App.csproj", model.Projects[0].FilePath);
        Assert.Equal("/", model.Projects[1].SolutionFolder);
        Assert.Equal(["/src/"], model.Folders);
    }

    [Fact]
    public void ProjectFile_ReadsSetsAndRemovesPropertiesKeepingFormatting()
    {
        var project = new ProjectFile("/work/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
                <PackageReference Include="Serilog" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """);

        project = project.SetProperty("TargetFramework", "net9.0").SetProperty("Nullable", "enable").SetProperty("OutputType", null);

        Assert.Equal("net9.0", project.Property("TargetFramework"));
        Assert.Equal("enable", project.Property("Nullable"));
        Assert.Null(project.Property("OutputType"));
        Assert.Contains("    <Nullable>enable</Nullable>\n  </PropertyGroup>", project.Text);
        Assert.Equal(["/work/Lib/Lib.csproj"], project.ProjectReferences());
        Assert.Equal(["Serilog"], project.PackageReferences().Select(p => p.Id));
        Assert.False(project.IsTestProject);
    }

    [Fact]
    public void LaunchProfiles_ParseWithCommentsAndTrailingCommas()
    {
        const string json = """
            {
              // comment
              "profiles": {
                "App": { "commandName": "Project", "commandLineArgs": "--verbose", "environmentVariables": { "MODE": "dev" }, },
                "IIS Express": { "commandName": "IISExpress" }
              }
            }
            """;

        var profiles = LaunchProfile.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(["App", "IIS Express"], profiles.Select(p => p.Name));
        Assert.True(profiles[0].IsRunnable && !profiles[1].IsRunnable);
        Assert.Equal(new Dictionary<string, string> { ["MODE"] = "dev" }, profiles[0].Environment);
        Assert.Equal(["--launch-profile", "App"], BuildService.RunArguments("/a/App.csproj", "Debug", profiles[0]).TakeLast(2));
    }

    [Fact]
    public void Trx_ParsesOutcomesDurationAndSourceFrame()
    {
        var results = TestService.ParseTrx(File.ReadAllBytes(Repository.Fixture("sample.trx")));

        Assert.Equal([TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Skipped], results.Select(r => r.Outcome));
        Assert.Equal(1.5, results[1].Duration);
        Assert.Equal("Assert.Equal() Failure", results[1].ErrorMessage);
        var frame = TestService.FirstSourceFrame(results[1].StackTrace);
        Assert.Equal(("/src/Calc.Tests/MathTests.cs", 21), frame);
    }

    [Fact]
    public void TestList_TakesIndentedNamesAfterTheLastHeader()
    {
        var output = new[] { "Build succeeded.", "The following Tests are available:", "    Calc.Tests.A", "    Calc.Tests.B(x: 1)", string.Empty, "done" }.Select(l => new OutputLine(l));

        Assert.Equal(["Calc.Tests.A", "Calc.Tests.B(x: 1)"], TestService.ParseTestList(output));
    }

    [Fact]
    public void TestNameAndFilter()
    {
        var name = new TestName("Calc.Tests.MathTests.Add(a: 1)");

        Assert.Equal(("Calc.Tests", "MathTests", "Add"), (name.Namespace, name.ClassName, name.Method));
        Assert.Equal("(a: 1)", name.Arguments);
        Assert.Equal("FullyQualifiedName=Calc.Tests.MathTests.Add|FullyQualifiedName~Calc.Other.", TestService.Filter(["Calc.Tests.MathTests.Add(a: 1)"], ["Calc.Other."]));
    }

    [Fact]
    public void Templates_ParseTableAndPreferCommonOnes()
    {
        const string output = """
            These templates matched your input: --type=project

            Template Name     Short Name        Language  Tags
            ----------------  ----------------  --------  --------------
            Class Library     classlib          [C#],F#   Common/Library
            Console App       console           [C#],F#   Common/Console
            WPF Application   wpf               [C#],VB   Common/WPF
            Worker Service    worker            [C#],F#   Common/Worker
            """;

        var templates = ProjectService.ParseTemplates(output);

        Assert.Equal(["console", "classlib", "worker"], templates.Select(t => t.ShortName));
        Assert.Equal("Console App", templates[0].Name);
    }

    [Fact]
    public void PackageList_ParsesTopLevelPackages()
    {
        const string json = """
            warning: something
            { "projects": [ { "frameworks": [ { "topLevelPackages": [ { "id": "Serilog", "requestedVersion": "4.0.0", "resolvedVersion": "4.0.0", "latestVersion": "4.1.0" } ] } ] } ] }
            """;

        var packages = ProjectService.ParsePackageList(json);

        Assert.Equal(["Serilog"], packages.Select(p => p.Id));
        Assert.True(packages[0].HasUpdate);
    }

    [Fact]
    public void SdkList_ParsesNewestFirst()
    {
        var sdks = DotnetLocator.ParseSdks("9.0.100 [/usr/share/dotnet/sdk]\n10.0.402 [/usr/share/dotnet/sdk]\n10.0.100-rc.1 [/x]");

        Assert.Equal(["10.0.402", "10.0.100-rc.1", "9.0.100"], sdks.Select(s => s.FullVersion));
        Assert.True(sdks[0].IsSupported && !sdks[2].IsSupported);
    }

    [Fact]
    public void TargetFrameworks_OnePerSdkMajorMinorNewestFirst()
    {
        var options = DotnetLocator.TargetFrameworks(DotnetLocator.ParseSdks("9.0.100 [/a]\n10.0.401 [/a]\n10.0.402 [/a]"));

        Assert.Equal(["net10.0", "net9.0"], options.Select(o => o.Moniker));
        Assert.Equal(["10.0.402", "10.0.401"], options[0].SdkVersions);
    }

    [Fact]
    public void SolutionSettings_RoundTrip()
    {
        var folder = Repository.TemporaryFolder("vslike-settings");
        try
        {
            var solution = Path.Combine(folder, "Demo.slnx");
            var settings = new SolutionSettings
            {
                StyleCopEnabled = true,
                Breakpoints = new Dictionary<string, IReadOnlyList<SourceBreakpoint>> { ["/a.cs"] = [new SourceBreakpoint(3, "x > 1")] },
            };

            settings.Save(solution);

            Assert.Equal(settings, SolutionSettings.Load(solution));
        }
        finally
        {
            Repository.Delete(folder);
        }
    }
}
