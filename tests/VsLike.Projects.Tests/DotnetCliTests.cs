using VsLike.Projects.Cli;

namespace VsLike.Projects.Tests;

public class DotnetCliTests
{
    [Fact]
    public void Parse_ReadsVersionsAndPathsSortedDescending()
    {
        const string output = "8.0.404 [/usr/local/share/dotnet/sdk]\n10.0.402 [/usr/local/share/dotnet/sdk]\n11.0.100-preview.1.25 [/x/sdk]\n";

        var sdks = DotnetSdks.Parse(output);

        Assert.Equal(["11.0.100-preview.1.25", "10.0.402", "8.0.404"], sdks.Select(s => s.VersionText));
        Assert.Equal("/x/sdk", sdks[0].Path);
        Assert.True(DotnetSdks.HasSupportedSdk(sdks));
        Assert.False(DotnetSdks.HasSupportedSdk(DotnetSdks.Parse(output.Split('\n')[0])));
    }

    [Theory]
    [InlineData("MSBuildExtensionsPath", true)]
    [InlineData("MSBUILD_EXE_PATH", true)]
    [InlineData("VSTEST_HOST", true)]
    [InlineData("DOTNET_HOST_PATH", true)]
    [InlineData("PATH", false)]
    [InlineData("DOTNET_ROOT", false)]
    public void CreateStartInfo_RemovesInheritedBuildVariables(string key, bool removed)
    {
        Assert.Equal(removed, DotnetCli.IsInheritedBuildVariable(key));
    }

    [Fact]
    public async Task RunAsync_ListsInstalledSdks()
    {
        var sdks = await DotnetSdks.ListAsync(new DotnetCli(), CancellationToken.None);

        Assert.True(DotnetSdks.HasSupportedSdk(sdks));
    }

    [Fact]
    public async Task RunAsync_CancellationKillsProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await new DotnetCli().RunAsync(["fsi"], null, null, cancellation.Token);

        Assert.True(result.WasCancelled || result.ExitCode != 0);
    }
}
