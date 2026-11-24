using Avalonia;
using Avalonia.Headless;
using VsLike.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace VsLike.Desktop.Tests;

/// <summary>Headless Avalonia application for tests that need the UI thread (the workspace lives there).</summary>
public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
