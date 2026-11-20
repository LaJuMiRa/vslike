using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Core.Platform;

namespace VsLike.App.Tests;

public sealed class MainWindowTests : IDisposable
{
    private readonly TestPlatform _platform = new(PlatformKind.Linux);

    [AvaloniaFact]
    public void Window_ShowsDockAndInWindowMenuOnLinux()
    {
        var (window, _) = Show();

        var menu = window.FindControl<Menu>("InWindowMenu");
        Assert.NotNull(menu);
        Assert.True(menu.IsVisible);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockControl>());
    }

    [AvaloniaFact]
    public void PaletteShortcut_OpensPaletteAndEscapeClosesIt()
    {
        var (window, shell) = Show();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(shell.Palette.IsOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(shell.Palette.IsOpen);
    }

    [AvaloniaFact]
    public void LanguageSwitch_UpdatesBoundTexts()
    {
        var (window, _) = Show();
        Loc.SetCulture("en");
        var menu = window.FindControl<Menu>("InWindowMenu")!;
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string?)i.Header == "View");

        Loc.SetCulture("de");

        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string?)i.Header == "Ansicht");
        Loc.SetCulture("en");
    }

    public void Dispose() => _platform.Dispose();

    private (MainWindow Window, ShellViewModel Shell) Show()
    {
        var services = new AppServices(_platform);
        var shell = new ShellViewModel(services, ShellPanels.CreateDefault());
        var window = new MainWindow(services, shell);
        window.Show();
        return (window, shell);
    }
}
