using System.Diagnostics;
using VsLike.Debugging.Netcoredbg;

namespace VsLike.Debugging.Tests;

/// <summary>Abnahme Phase 9: Breakpoint in einer Console-App trifft, Variablen sichtbar, Stepping funktioniert.</summary>
public sealed class NetcoredbgIntegrationTests : IAsyncLifetime
{
    private const string Program = """
        var total = 0;
        for (var i = 0; i < 5; i++)
        {
            total += Add(i, 1);
        }

        Console.WriteLine($"total={total}");

        static int Add(int a, int b)
        {
            return a + b;
        }
        """;

    private readonly string _root = Path.Combine(VsLike.Core.Files.RealPath.Resolve(Path.GetTempPath()), "vslike-debug", Guid.NewGuid().ToString("N"));
    private string _program = string.Empty;
    private string _netcoredbg = string.Empty;

    public async Task InitializeAsync()
    {
        using var http = new HttpClient();
        _netcoredbg = await NetcoredbgPackage.EnsureAsync(http, CancellationToken.None);
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "Dbg.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(_root, "Program.cs"), Program);
        var build = Process.Start(new ProcessStartInfo("dotnet", ["build", _root, "-c", "Debug", "-nologo"]) { RedirectStandardOutput = true, UseShellExecute = false })!;
        var log = await build.StandardOutput.ReadToEndAsync();
        await build.WaitForExitAsync();
        Assert.True(build.ExitCode == 0, log);
        _program = Path.Combine(_root, "bin", "Debug", "net10.0", "Dbg.dll");
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConditionalBreakpoint_Variables_Stepping_Evaluate()
    {
        var source = Path.Combine(_root, "Program.cs");
        var channel = System.Threading.Channels.Channel.CreateUnbounded<StoppedInfo>();
        var output = new List<string>();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var breakpoints = new Dictionary<string, IReadOnlyList<SourceBreakpoint>> { [source] = [new SourceBreakpoint(4, "i == 3")] };

        var (session, states) = await DebugSession.StartAsync(
            _netcoredbg,
            new LaunchRequest(_program, [], _root, new Dictionary<string, string>()),
            breakpoints,
            DotnetDirectory(),
            s =>
            {
                s.Stopped += (_, e) => channel.Writer.TryWrite(e);
                s.Output += (_, e) =>
                {
                    lock (output)
                    {
                        output.Add(e.Text);
                    }
                };
                s.Exited += (_, code) => exited.TrySetResult(code);
            },
            CancellationToken.None);
        await using var _ = session;
        Assert.Single(states[source]);

        StoppedInfo stop;
        try
        {
            stop = await NextStopAsync(channel);
        }
        catch (TimeoutException)
        {
            lock (output)
            {
                Assert.Fail($"no stop; exited={exited.Task.IsCompleted}; output:\n{string.Join(string.Empty, output)}");
            }

            throw;
        }

        Assert.Equal("breakpoint", stop.Reason);
        var frame = (await session.StackTraceAsync(stop.ThreadId, CancellationToken.None))[0];
        Assert.Equal((source, 4), (frame.FilePath, frame.Line));
        var locals = await session.VariablesAsync((await session.ScopesAsync(frame.Id, CancellationToken.None))[0].VariablesReference, CancellationToken.None);
        Assert.Contains(locals, v => v is { Name: "i", Value: "3" });
        Assert.Contains(locals, v => v is { Name: "total", Value: "6" });
        Assert.Equal("7", (await session.EvaluateAsync("total + 1", frame.Id, "watch", CancellationToken.None)).Value);
        Assert.Contains(await session.ThreadsAsync(CancellationToken.None), t => t.Id == stop.ThreadId);

        await session.StepInAsync(stop.ThreadId, CancellationToken.None);
        stop = await NextStopAsync(channel);
        var inner = (await session.StackTraceAsync(stop.ThreadId, CancellationToken.None))[0];
        Assert.Contains("Add", inner.Name, StringComparison.Ordinal);
        Assert.InRange(inner.Line, 10, 11);

        await session.StepOutAsync(stop.ThreadId, CancellationToken.None);
        stop = await NextStopAsync(channel);
        Assert.Equal(4, (await session.StackTraceAsync(stop.ThreadId, CancellationToken.None))[0].Line);

        await session.SetBreakpointsAsync(source, [], CancellationToken.None);
        await session.ContinueAsync(stop.ThreadId, CancellationToken.None);
        Assert.Equal(0, await exited.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        lock (output)
        {
            Assert.Contains(output, o => o.Contains("total=15", StringComparison.Ordinal));
        }
    }

    private static async Task<StoppedInfo> NextStopAsync(System.Threading.Channels.Channel<StoppedInfo> channel)
        => await channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));

    private static string? DotnetDirectory()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Append("/usr/local/share/dotnet").Append("/usr/share/dotnet");
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "dotnet")));
    }
}
