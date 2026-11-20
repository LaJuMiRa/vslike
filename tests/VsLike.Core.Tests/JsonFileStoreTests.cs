using VsLike.Core.Layout;
using VsLike.Core.Settings;

namespace VsLike.Core.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vslike-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsDefaultsWhenFileIsMissing()
    {
        var store = new JsonFileStore<AppSettings>(Path.Combine(_directory, "settings.json"));

        Assert.Equal(ThemeMode.System, store.Load().Theme);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var store = new JsonFileStore<AppSettings>(Path.Combine(_directory, "settings.json"));
        var settings = new AppSettings { Theme = ThemeMode.Light, Language = "de" }.WithRecentSolution("/a.slnx");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(ThemeMode.Light, loaded.Theme);
        Assert.Equal("de", loaded.Language);
        Assert.Equal(["/a.slnx"], loaded.RecentSolutions);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsLayout()
    {
        var store = new JsonFileStore<LayoutState>(Path.Combine(_directory, "layout.json"));
        var layout = new LayoutState
        {
            Panels = [new PanelPlacement("panel.output", "right", 0, false, true)],
            Proportions = new Dictionary<string, double> { ["left"] = 0.3 },
        };

        store.Save(layout);
        var loaded = store.Load();

        Assert.Equal(layout.Panels, loaded.Panels);
        Assert.Equal(0.3, loaded.Proportions["left"]);
    }

    [Fact]
    public void Load_ReturnsDefaultsAndKeepsBackupForBrokenFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new JsonFileStore<AppSettings>(path).Load();

        Assert.Equal(ThemeMode.System, loaded.Theme);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void WithRecentSolution_MovesExistingEntryToFrontWithoutMutating()
    {
        var original = new AppSettings { RecentSolutions = ["/a", "/b"] };

        var updated = original.WithRecentSolution("/b");

        Assert.Equal(["/b", "/a"], updated.RecentSolutions);
        Assert.Equal(["/a", "/b"], original.RecentSolutions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
