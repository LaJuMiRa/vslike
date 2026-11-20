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
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .AfterSetup(_ =>
            {
                Avalonia.Logging.Logger.Sink = new RecordingLogSink();
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => UiWait.Record("dispatcher", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (_, e) => UiWait.Record("domain", e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (_, e) => UiWait.Record("task", e.Exception);
            });
}
