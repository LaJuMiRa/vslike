using Avalonia;

namespace VsLike.Desktop;

/// <summary>
/// Entry point. Placeholder until the app shell (port of VsLikeApp.swift) replaces it: starts Avalonia with an empty
/// main window so that the model layer builds and runs.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
