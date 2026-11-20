using VsLike.Projects.Build;

namespace VsLike.Projects.Tests;

public sealed class LaunchSettingsReaderTests
{
    [Fact]
    public void Parse_ReadsProfilesWithCommentsAndTrailingCommas()
    {
        const string json = """
            {
              // comment
              "profiles": {
                "App": {
                  "commandName": "Project",
                  "commandLineArgs": "--verbose",
                  "environmentVariables": { "DOTNET_ENVIRONMENT": "Development", },
                  "applicationUrl": "http://localhost:5000",
                },
                "IIS Express": { "commandName": "IISExpress" }
              }
            }
            """;

        var profiles = LaunchSettingsReader.Parse(json);

        Assert.Equal(["App", "IIS Express"], profiles.Select(p => p.Name));
        var app = profiles[0];
        Assert.True(app.IsRunnable);
        Assert.Equal("--verbose", app.CommandLineArgs);
        Assert.Equal("Development", app.EnvironmentVariables["DOTNET_ENVIRONMENT"]);
        Assert.Equal("http://localhost:5000", app.ApplicationUrl);
        Assert.False(profiles[1].IsRunnable);
    }

    [Fact]
    public void Read_MissingOrBrokenFile_ReturnsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vslike-launch", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(directory, "App.csproj");
        Directory.CreateDirectory(Path.Combine(directory, "Properties"));
        try
        {
            Assert.Empty(LaunchSettingsReader.Read(project));
            File.WriteAllText(LaunchSettingsReader.PathFor(project), "{ broken");
            Assert.Empty(LaunchSettingsReader.Read(project));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RunArguments_UseProfileOrDisableLaunchSettings()
    {
        var profile = LaunchSettingsReader.Parse("""{ "profiles": { "Dev": { "commandName": "Project" } } }""")[0];
        Assert.Equal(["run", "--project", "/a/App.csproj", "-c", "Release", "--no-build", "--launch-profile", "Dev"], BuildService.RunArguments("/a/App.csproj", "Release", profile));
        Assert.Contains("--no-launch-profile", BuildService.RunArguments("/a/App.csproj", "Debug", null));
        Assert.Contains("--no-incremental", BuildService.BuildArguments("/a/S.slnx", "Debug", BuildKind.Rebuild));
        Assert.Equal("clean", BuildService.BuildArguments("/a/S.slnx", "Debug", BuildKind.Clean)[0]);
    }
}
