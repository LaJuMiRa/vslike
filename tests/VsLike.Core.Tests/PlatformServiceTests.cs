using VsLike.Core.Platform;

namespace VsLike.Core.Tests;

public class PlatformServiceTests
{
    [Fact]
    public void MacOS_UsesApplicationSupportAndCommandKey()
    {
        var service = new PlatformService(PlatformKind.MacOS, "/Users/test");

        Assert.Equal("/Users/test/Library/Application Support/VsLike", service.SettingsDirectory);
        Assert.True(service.HasNativeMenuBar);
        Assert.Equal("⌘", service.PrimaryModifierDisplay);
    }

    [Fact]
    public void Linux_UsesConfigDirectoryAndCtrl()
    {
        var service = new PlatformService(PlatformKind.Linux, "/home/test");

        Assert.EndsWith("vslike", service.SettingsDirectory, StringComparison.Ordinal);
        Assert.False(service.HasNativeMenuBar);
        Assert.Equal("Ctrl", service.PrimaryModifierDisplay);
    }
}
