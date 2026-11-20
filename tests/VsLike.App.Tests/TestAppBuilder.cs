using Avalonia;
using Avalonia.Headless;
using VsLike.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// UI-Tests teilen statischen Zustand (Sprache, Application.Current).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VsLike.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
