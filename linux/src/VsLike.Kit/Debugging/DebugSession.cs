using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace VsLike.Kit;

public sealed record SourceBreakpoint(int Line, string? Condition = null);

public sealed record BreakpointState(int Line, bool Verified, string? Message);

public sealed record DebugThread(int Id, string Name);

public sealed record StackFrame(int Id, string Name, string? FilePath, int Line, int Column);

public sealed record DebugScope(string Name, int VariablesReference);

public sealed record DebugVariable(string Name, string Value, string? Type, int VariablesReference)
{
    public string Id => $"{Name}#{VariablesReference}#{Value}";
}

public sealed record LaunchConfiguration(
    string Program,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    bool StopAtEntry = false);

public abstract record DebugEvent
{
    public sealed record Stopped(string Reason, int ThreadId, string? Description) : DebugEvent;

    public sealed record Continued : DebugEvent;

    public sealed record Output(string Category, string Text) : DebugEvent;

    public sealed record Exited(int Code) : DebugEvent;

    public sealed record Terminated : DebugEvent;
}

public class DebugException(string message) : Exception(message)
{
    public static DebugException AdapterNotFound() => new("The debugger (netcoredbg) was not found in the app folder.");

    public static DebugException AdapterFailed(string message) => new($"The debugger could not be started: {message}");

    public static DebugException Request(string command, string message) => new($"{command}: {message}");
}

/// <summary>
/// A debug session with netcoredbg over the Debug Adapter Protocol (<c>--interpreter=vscode</c>). Events arrive on
/// <see cref="Events"/>; requests are async. vsdbg is never used (its licence forbids it).
/// </summary>
public sealed partial class DebugSession
{
    private readonly Process _process = new();
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<DebugEvent> _events = Channel.CreateUnbounded<DebugEvent>();
    private readonly MessageFramer _framer = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _pending = [];
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Stream? _input;
    private int _nextSeq;

    private DebugSession()
    {
    }

    public ChannelReader<DebugEvent> Events => _events.Reader;

    /// <summary>
    /// netcoredbg in the install folder (<c>netcoredbg/netcoredbg</c>), <c>VSLIKE_NETCOREDBG</c> for development, or the
    /// newest version unpacked into <c>$XDG_CACHE_HOME/vslike/netcoredbg</c> by the build script.
    /// </summary>
    public static string? LocateAdapter()
    {
        var candidates = new List<string>();
        if (System.Environment.GetEnvironmentVariable("VSLIKE_NETCOREDBG") is { } overridePath)
        {
            candidates.Add(overridePath);
        }

        candidates.Add(Path.Combine(AppPaths.InstallFolder, "netcoredbg", "netcoredbg"));
        var cache = Path.Combine(AppPaths.CacheFolder, "netcoredbg");
        if (Directory.Exists(cache))
        {
            candidates.AddRange(Directory.GetDirectories(cache).Select(Path.GetFileName).OrderDescending(StringComparer.Ordinal)
                .Select(v => Path.Combine(cache, v!, "netcoredbg", "netcoredbg")));
        }

        return candidates.FirstOrDefault(IsExecutable);
    }

    /// <summary>
    /// Starts netcoredbg, launches the program, sets the breakpoints and runs. Read <see cref="Events"/> right after
    /// this returns; events that arrived earlier are buffered. The program's output arrives as output events and it
    /// cannot read input; <see cref="Attach"/> is the variant for programs that run in a terminal.
    /// </summary>
    public static Task<(DebugSession Session, IReadOnlyDictionary<string, IReadOnlyList<BreakpointState>> Breakpoints)> Start(
        string adapter,
        LaunchConfiguration launch,
        IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>> breakpoints,
        string? dotnet)
    {
        var environment = new JsonObject();
        foreach (var (key, value) in launch.Environment)
        {
            environment[key] = value;
        }

        return Connect(adapter, dotnet, breakpoints, "launch", new JsonObject
        {
            ["name"] = "VsLike",
            ["type"] = "coreclr",
            ["request"] = "launch",
            ["program"] = launch.Program,
            ["args"] = new JsonArray([.. launch.Arguments.Select(a => (JsonNode?)JsonValue.Create(a))]),
            ["cwd"] = launch.WorkingDirectory,
            ["env"] = environment,
            ["stopAtEntry"] = launch.StopAtEntry,
            ["console"] = "internalConsole",
            ["justMyCode"] = true,
        });
    }

    /// <summary>
    /// Attaches netcoredbg to a running .NET process (a program started in a terminal), sets the breakpoints and
    /// then creates <paramref name="readyFile"/>, which releases a program held by the debug startup hook.
    /// </summary>
    public static async Task<(DebugSession Session, IReadOnlyDictionary<string, IReadOnlyList<BreakpointState>> Breakpoints)> Attach(
        string adapter,
        int processId,
        IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>> breakpoints,
        string? dotnet,
        string? readyFile)
    {
        var result = await Connect(adapter, dotnet, breakpoints, "attach", new JsonObject
        {
            ["name"] = "VsLike",
            ["type"] = "coreclr",
            ["request"] = "attach",
            ["processId"] = processId,
            ["justMyCode"] = true,
        }).ConfigureAwait(false);
        if (readyFile is not null)
        {
            await File.WriteAllBytesAsync(readyFile, []).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<IReadOnlyList<BreakpointState>> SetBreakpoints(string file, IReadOnlyList<SourceBreakpoint> breakpoints)
    {
        var list = new JsonArray();
        foreach (var breakpoint in breakpoints)
        {
            var entry = new JsonObject { ["line"] = breakpoint.Line };
            if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            {
                entry["condition"] = breakpoint.Condition;
            }

            list.Add(entry);
        }

        var body = await Request("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = file, ["name"] = Path.GetFileName(file) },
            ["breakpoints"] = list,
        }).ConfigureAwait(false);
        return body.ArrayValue("breakpoints").Select((item, index) => new BreakpointState(
            item.IntValue("line") ?? (index < breakpoints.Count ? breakpoints[index].Line : 0),
            item.BoolValue("verified") ?? false,
            item.StringValue("message"))).ToList();
    }

    public async Task<IReadOnlyList<DebugThread>> Threads()
    {
        var body = await Request("threads", null).ConfigureAwait(false);
        return [.. body.ArrayValue("threads").Select(t => new DebugThread(t.IntValue("id") ?? 0, t.StringValue("name") ?? string.Empty))];
    }

    public async Task<IReadOnlyList<StackFrame>> StackTrace(int threadId)
    {
        var body = await Request("stackTrace", new JsonObject { ["threadId"] = threadId, ["startFrame"] = 0, ["levels"] = 100 }).ConfigureAwait(false);
        return body.ArrayValue("stackFrames").Select(f => new StackFrame(
            f.IntValue("id") ?? 0,
            f.StringValue("name") ?? string.Empty,
            f.Child("source").StringValue("path"),
            f.IntValue("line") ?? 0,
            f.IntValue("column") ?? 0)).ToList();
    }

    public async Task<IReadOnlyList<DebugScope>> Scopes(int frameId)
    {
        var body = await Request("scopes", new JsonObject { ["frameId"] = frameId }).ConfigureAwait(false);
        return [.. body.ArrayValue("scopes").Select(s => new DebugScope(s.StringValue("name") ?? string.Empty, s.IntValue("variablesReference") ?? 0))];
    }

    public async Task<IReadOnlyList<DebugVariable>> Variables(int reference)
    {
        var body = await Request("variables", new JsonObject { ["variablesReference"] = reference }).ConfigureAwait(false);
        return body.ArrayValue("variables").Select(v => new DebugVariable(
            v.StringValue("name") ?? string.Empty,
            v.StringValue("value") ?? string.Empty,
            v.StringValue("type"),
            v.IntValue("variablesReference") ?? 0)).ToList();
    }

    /// <param name="context">"watch", "hover" or "repl".</param>
    public async Task<DebugVariable> Evaluate(string expression, int? frameId, string context)
    {
        var arguments = new JsonObject { ["expression"] = expression, ["context"] = context };
        if (frameId is { } id)
        {
            arguments["frameId"] = id;
        }

        var body = await Request("evaluate", arguments).ConfigureAwait(false);
        return new DebugVariable(expression, body.StringValue("result") ?? string.Empty, body.StringValue("type"), body.IntValue("variablesReference") ?? 0);
    }

    public Task Resume(int threadId) => Step("continue", threadId);

    public Task StepOver(int threadId) => Step("next", threadId);

    public Task StepIn(int threadId) => Step("stepIn", threadId);

    public Task StepOut(int threadId) => Step("stepOut", threadId);

    public Task Pause(int threadId) => Request("pause", new JsonObject { ["threadId"] = threadId });

    /// <summary>Ends the debuggee and the adapter.</summary>
    public async Task Stop()
    {
        var disconnect = Request("disconnect", new JsonObject { ["terminateDebuggee"] = true });
        await Task.WhenAny(disconnect, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        _ = disconnect.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        Kill();
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Never started or already ended.
        }
    }

    private static bool IsExecutable(string path) =>
        File.Exists(path) && (OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0);
}

/// <summary>Process handling and the DAP message loop.</summary>
public sealed partial class DebugSession
{
    private static async Task<(DebugSession Session, IReadOnlyDictionary<string, IReadOnlyList<BreakpointState>> Breakpoints)> Connect(
        string adapter,
        string? dotnet,
        IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>> breakpoints,
        string command,
        JsonObject arguments)
    {
        var session = new DebugSession();
        session.Spawn(adapter, dotnet);
        try
        {
            await session.Request("initialize", new JsonObject
            {
                ["clientID"] = "vslike",
                ["clientName"] = "VsLike",
                ["adapterID"] = "coreclr",
                ["linesStartAt1"] = true,
                ["columnsStartAt1"] = true,
                ["pathFormat"] = "path",
                ["supportsVariableType"] = true,
            }).ConfigureAwait(false);
            var startTask = session.Request(command, arguments);
            await session.WaitForInitialized(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var states = new Dictionary<string, IReadOnlyList<BreakpointState>>();
            foreach (var (file, list) in breakpoints)
            {
                states[file] = await session.SetBreakpoints(file, list).ConfigureAwait(false);
            }

            await session.Request("configurationDone", null).ConfigureAwait(false);
            await startTask.ConfigureAwait(false);
            return (session, states);
        }
        catch
        {
            session.Kill();
            throw;
        }
    }

    private void Spawn(string adapter, string? dotnet)
    {
        var info = new ProcessStartInfo(adapter)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--interpreter=vscode");
        var environment = DotnetLocator.CurrentEnvironment()
            .Where(e => !e.Key.StartsWith("msbuild", StringComparison.OrdinalIgnoreCase) && !e.Key.StartsWith("VSTEST", StringComparison.Ordinal))
            .ToDictionary();
        if (dotnet is not null)
        {
            var folder = Path.GetDirectoryName(dotnet) ?? string.Empty;
            environment["PATH"] = folder + ":" + (environment.GetValueOrDefault("PATH") ?? "/usr/bin:/bin");
            environment["DOTNET_ROOT"] = folder;
        }

        info.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        _process.StartInfo = info;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => AdapterExited();
        try
        {
            _process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw DebugException.AdapterFailed(ex.Message);
        }

        _input = _process.StandardInput.BaseStream;
        _ = Task.Run(() => ReadLoop(_process.StandardOutput.BaseStream));
        _ = Task.Run(() => _process.StandardError.ReadToEndAsync());
    }

    private async Task WaitForInitialized(TimeSpan timeout)
    {
        var finished = await Task.WhenAny(_initialized.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != _initialized.Task)
        {
            throw DebugException.AdapterFailed($"no 'initialized' event within {(int)timeout.TotalSeconds} s");
        }
    }

    private async Task Step(string command, int threadId)
    {
        _events.Writer.TryWrite(new DebugEvent.Continued());
        await Request(command, new JsonObject { ["threadId"] = threadId }).ConfigureAwait(false);
    }

    private async Task<JsonNode?> Request(string command, JsonNode? arguments)
    {
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int seq;
        Stream? input;
        lock (_lock)
        {
            seq = ++_nextSeq;
            input = _input;
            if (input is not null)
            {
                _pending[seq] = completion;
            }
        }

        if (input is null)
        {
            throw DebugException.Request(command, "debugger not running");
        }

        var message = new JsonObject { ["seq"] = seq, ["type"] = "request", ["command"] = command };
        if (arguments is not null)
        {
            message["arguments"] = arguments;
        }

        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await input.WriteAsync(MessageFramer.Frame(body)).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // AdapterExited fails the request.
        }
        finally
        {
            _writeLock.Release();
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task ReadLoop(Stream output)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = await output.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                Receive(buffer.AsSpan(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Adapter ended.
        }
    }

    private void Receive(ReadOnlySpan<byte> data)
    {
        IReadOnlyList<byte[]> messages;
        lock (_lock)
        {
            messages = _framer.Append(data);
        }

        foreach (var body in messages)
        {
            JsonNode? message;
            try
            {
                message = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                continue;
            }

            switch (message.StringValue("type"))
            {
                case "response":
                    HandleResponse(message);
                    break;
                case "event":
                    HandleEvent(message.StringValue("event") ?? string.Empty, message.Child("body"));
                    break;
            }
        }
    }

    private void HandleResponse(JsonNode? message)
    {
        TaskCompletionSource<JsonNode?>? completion = null;
        if (message.IntValue("request_seq") is not { } seq)
        {
            return;
        }

        lock (_lock)
        {
            _pending.Remove(seq, out completion);
        }

        if (completion is null)
        {
            return;
        }

        if (message.BoolValue("success") == true)
        {
            completion.TrySetResult(message.Child("body")?.DeepClone());
        }
        else
        {
            var text = message.Child("body").Child("error").StringValue("format") ?? message.StringValue("message") ?? "failed";
            completion.TrySetException(DebugException.Request(message.StringValue("command") ?? string.Empty, text));
        }
    }

    private void HandleEvent(string name, JsonNode? body)
    {
        switch (name)
        {
            case "initialized":
                _initialized.TrySetResult();
                break;
            case "stopped":
                _events.Writer.TryWrite(new DebugEvent.Stopped(body.StringValue("reason") ?? string.Empty, body.IntValue("threadId") ?? 0, body.StringValue("description")));
                break;
            case "continued":
                _events.Writer.TryWrite(new DebugEvent.Continued());
                break;
            case "output":
                _events.Writer.TryWrite(new DebugEvent.Output(body.StringValue("category") ?? "console", body.StringValue("output") ?? string.Empty));
                break;
            case "exited":
                _events.Writer.TryWrite(new DebugEvent.Exited(body.IntValue("exitCode") ?? 0));
                break;
            case "terminated":
                _events.Writer.TryWrite(new DebugEvent.Terminated());
                break;
        }
    }

    private void AdapterExited()
    {
        List<TaskCompletionSource<JsonNode?>> waiting;
        lock (_lock)
        {
            _input = null;
            waiting = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var completion in waiting)
        {
            completion.TrySetException(DebugException.Request(string.Empty, "debugger ended"));
        }

        _events.Writer.TryWrite(new DebugEvent.Terminated());
        _events.Writer.TryComplete();
    }
}
