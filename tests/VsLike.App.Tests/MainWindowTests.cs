using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Core.Platform;

namespace VsLike.App.Tests;

public sealed class MainWindowTests : IDisposable
{
    private readonly TestPlatform _platform = new(PlatformKind.Linux);
    private readonly List<Workbench> _workbenches = [];

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
        Dispatcher.UIThread.RunJobs();
        var menu = window.FindControl<Menu>("InWindowMenu")!;
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string?)i.Header == "View");

        Loc.SetCulture("de");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string?)i.Header == "Ansicht");
        Loc.SetCulture("en");
    }

    [AvaloniaFact]
    public void DisposingWorkbench_ClosesItsWindow()
    {
        var (window, _) = Show();
        Assert.True(window.IsVisible);

        _workbenches[^1].Dispose();

        Assert.False(window.IsVisible);
    }

    public void Dispose()
    {
        _workbenches.ForEach(w => w.Dispose());
        _platform.Dispose();
    }

    private (MainWindow Window, ShellViewModel Shell) Show()
    {
        var workbench = Track(new Workbench(new AppServices(_platform), new FakeDialogService(), () => null));
        var window = new MainWindow(workbench);
        window.Show();
        return (window, workbench.Shell);
    }

    private Workbench Track(Workbench workbench)
    {
        _workbenches.Add(workbench);
        return workbench;
    }
}
