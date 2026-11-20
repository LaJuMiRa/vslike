using VsLike.Core.Platform;

namespace VsLike.App.Tests;

/// <summary>Plattform mit eigenem, temporärem Einstellungsordner.</summary>
internal sealed class TestPlatform : IPlatformService, IDisposable
{
    public TestPlatform(PlatformKind kind = PlatformKind.MacOS)
    {
        Kind = kind;
        SettingsDirectory = Path.Combine(Path.GetTempPath(), "vslike-app-tests", Guid.NewGuid().ToString("N"));
    }

    public PlatformKind Kind { get; }

    public string SettingsDirectory { get; }

    public bool HasNativeMenuBar => Kind == PlatformKind.MacOS;

    public string PrimaryModifierDisplay => Kind == PlatformKind.MacOS ? "⌘" : "Ctrl";

    public void Dispose()
    {
        if (Directory.Exists(SettingsDirectory))
        {
            Directory.Delete(SettingsDirectory, recursive: true);
        }
    }
}
