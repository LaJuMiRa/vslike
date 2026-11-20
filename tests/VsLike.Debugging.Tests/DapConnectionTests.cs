using System.Text;
using VsLike.Debugging.Dap;

namespace VsLike.Debugging.Tests;

public sealed class DapConnectionTests
{
    [Fact]
    public async Task Frames_RoundTripIncludingUtf8()
    {
        using var stream = new MemoryStream([.. DapConnection.Frame("{\"a\":\"ä\"}"), .. DapConnection.Frame("{\"b\":2}")]);

        Assert.Equal("{\"a\":\"ä\"}", await DapConnection.ReadMessageAsync(stream, CancellationToken.None));
        Assert.Equal("{\"b\":2}", await DapConnection.ReadMessageAsync(stream, CancellationToken.None));
        Assert.Null(await DapConnection.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Frame_UsesByteLength()
    {
        var frame = Encoding.UTF8.GetString(DapConnection.Frame("ü"));
        Assert.StartsWith("Content-Length: 2\r\n\r\n", frame, StringComparison.Ordinal);
    }
}
