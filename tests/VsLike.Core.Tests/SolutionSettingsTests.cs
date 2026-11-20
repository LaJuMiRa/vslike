using VsLike.Core.Settings;

namespace VsLike.Core.Tests;

public sealed class SolutionSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vslike-solution-settings", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WithoutFile_ReturnsDefaults()
    {
        var settings = SolutionSettings.Load(Path.Combine(_root, "A.slnx"));

        Assert.Equal("Debug", settings.Configuration);
        Assert.Null(settings.StartupProject);
        Assert.Null(settings.StyleCopEnabled);
    }

    [Fact]
    public void Save_WritesBesideSolutionAndRoundTrips()
    {
        var solution = Path.Combine(_root, "A.slnx");
        new SolutionSettings { Configuration = "Release", StartupProject = "App/App.csproj", LaunchProfile = "Dev", StyleCopEnabled = true }.Save(solution);

        Assert.True(File.Exists(Path.Combine(_root, ".vslike", "settings.json")));
        var loaded = SolutionSettings.Load(solution);
        Assert.Equal(("Release", "App/App.csproj", "Dev", true), (loaded.Configuration, loaded.StartupProject, loaded.LaunchProfile, loaded.StyleCopEnabled));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
