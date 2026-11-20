using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Core.Settings;

namespace VsLike.App.Tests;

public sealed class ShellViewModelTests : IDisposable
{
    private readonly TestPlatform _platform = new();

    [Fact]
    public async Task TogglePanelCommand_HidesAndShowsPanel()
    {
        var services = new AppServices(_platform);
        var shell = new ShellViewModel(services, ShellPanels.CreateDefault());

        await services.Commands.ExecuteAsync("view.panel.output");
        Assert.False(shell.IsPanelVisible("output"));

        await services.Commands.ExecuteAsync("view.panel.output");
        Assert.True(shell.IsPanelVisible("output"));
    }

    [Fact]
    public async Task SaveLayout_IsRestoredByNewShell()
    {
        var services = new AppServices(_platform);
        var shell = new ShellViewModel(services, ShellPanels.CreateDefault());
        await services.Commands.ExecuteAsync("view.panel.explorer");
        shell.SaveLayout(1000, 700, maximized: false);

        var restored = new ShellViewModel(new AppServices(_platform), ShellPanels.CreateDefault());

        Assert.False(restored.IsPanelVisible("explorer"));
        Assert.Equal(1000, restored.LayoutState.WindowWidth);
    }

    [Fact]
    public async Task ResetLayout_ShowsAllPanelsAgain()
    {
        var services = new AppServices(_platform);
        var shell = new ShellViewModel(services, ShellPanels.CreateDefault());
        await services.Commands.ExecuteAsync("view.panel.explorer");

        await services.Commands.ExecuteAsync("window.layout.reset");

        Assert.True(shell.IsPanelVisible("explorer"));
    }

    [AvaloniaFact]
    public async Task ThemeCommand_PersistsSetting()
    {
        var services = new AppServices(_platform);
        _ = new ShellViewModel(services, ShellPanels.CreateDefault());

        await services.Commands.ExecuteAsync("view.theme.light");

        Assert.Equal(ThemeMode.Light, new AppServices(_platform).Settings.Theme);
    }

    [Fact]
    public async Task Palette_FiltersAndExecutesSelectedCommand()
    {
        Loc.SetCulture("en");
        var services = new AppServices(_platform);
        var shell = new ShellViewModel(services, ShellPanels.CreateDefault());

        await services.Commands.ExecuteAsync(ShellViewModel.PaletteCommandId);
        Assert.True(shell.Palette.IsOpen);
        shell.Palette.Query = "reset layout";

        Assert.Equal("window.layout.reset", shell.Palette.SelectedItem?.CommandId);
        Assert.True(await shell.Palette.ExecuteSelectedAsync());
        Assert.False(shell.Palette.IsOpen);
    }

    [Fact]
    public void Linux_RegistersQuitCommand()
    {
        using var linux = new TestPlatform(Core.Platform.PlatformKind.Linux);
        var services = new AppServices(linux);
        _ = new ShellViewModel(services, ShellPanels.CreateDefault());

        Assert.NotNull(services.Commands.Find("app.quit"));
        Assert.True(services.Menus.Contains("app.quit"));
    }

    public void Dispose() => _platform.Dispose();
}
