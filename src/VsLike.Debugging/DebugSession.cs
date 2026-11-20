using System.Diagnostics;
using System.Text.Json;
using VsLike.Debugging.Dap;

namespace VsLike.Debugging;

/// <summary>
/// Eine Debug-Sitzung mit netcoredbg (<c>--interpreter=vscode</c>): Start, Breakpoints (auch bedingt), Schrittweise
/// Ausführung, Threads, Aufrufliste, Variablen, Auswertung. Ereignisse kommen auf einem Hintergrund-Thread.
/// </summary>
public sealed class DebugSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly DapConnection _connection;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    private DebugSession(Process process)
    {
        _process = process;
        _connection = new DapConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _connection.EventReceived += (_, e) => OnEvent(e.Event, e.Body);
        _connection.Closed += (_, _) => Terminated?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<StoppedInfo>? Stopped;

    public event EventHandler? Continued;

    public event EventHandler<OutputInfo>? Output;

    public event EventHandler<int>? Exited;

    public event EventHandler? Terminated;

    public bool IsRunning { get; private set; }

    /// <summary>Starts netcoredbg, launches the program, sets the breakpoints and runs.</summary>
    /// <param name="attach">Subscribes to events before the program starts (so no early stop is missed).</param>
    public static async Task<(DebugSession Session, IReadOnlyDictionary<string, IReadOnlyList<BreakpointState>> Breakpoints)> StartAsync(
        string netcoredbg,
        LaunchRequest launch,
        IReadOnlyDictionary<string, IReadOnlyList<SourceBreakpoint>> breakpoints,
        string? dotnetDirectory,
        Action<DebugSession>? attach,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(netcoredbg)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--interpreter=vscode");
        if (dotnetDirectory is not null)
        {
            start.Environment["PATH"] = dotnetDirectory + Path.PathSeparator + start.Environment["PATH"];
            start.Environment["DOTNET_ROOT"] = dotnetDirectory;
        }

        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) || k.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(key);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("netcoredbg could not be started.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        var session = new DebugSession(process);
        attach?.Invoke(session);
        session._connection.Start();
        try
        {
            await session._connection.RequestAsync("initialize", new { clientID = "vslike", clientName = "VsLike", adapterID = "coreclr", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path", supportsVariableType = true }, cancellationToken).ConfigureAwait(false);
            var launchTask = session._connection.RequestAsync(
                "launch",
                new
                {
                    name = "VsLike",
                    type = "coreclr",
                    request = "launch",
                    program = launch.Program,
                    args = launch.Arguments,
                    cwd = launch.WorkingDirectory,
                    env = launch.Environment,
                    stopAtEntry = launch.StopAtEntry,
                    console = "internalConsole",
                    justMyCode = true,
                },
                cancellationToken);
            await session._initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            var states = new Dictionary<string, IReadOnlyList<BreakpointState>>(StringComparer.Ordinal);
            foreach (var (file, list) in breakpoints)
            {
                states[file] = await session.SetBreakpointsAsync(file, list, cancellationToken).ConfigureAwait(false);
            }

            await session._connection.RequestAsync("configurationDone", null, cancellationToken).ConfigureAwait(false);
            await launchTask.ConfigureAwait(false);
            session.IsRunning = true;
            return (session, states);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<BreakpointState>> SetBreakpointsAsync(string file, IReadOnlyList<SourceBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync(
            "setBreakpoints",
            new
            {
                source = new { path = file, name = Path.GetFileName(file) },
                breakpoints = breakpoints.Select(b => new { line = b.Line, condition = string.IsNullOrWhiteSpace(b.Condition) ? null : b.Condition }).ToList(),
            },
            cancellationToken).ConfigureAwait(false);
        return body.GetProperty("breakpoints").EnumerateArray()
            .Select((b, i) => new BreakpointState(
                b.TryGetProperty("line", out var line) ? line.GetInt32() : breakpoints[i].Line,
                b.TryGetProperty("verified", out var verified) && verified.GetBoolean(),
                b.TryGetProperty("message", out var message) ? message.GetString() : null))
            .ToList();
    }

    public async Task<IReadOnlyList<DebugThread>> ThreadsAsync(CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync("threads", null, cancellationToken).ConfigureAwait(false);
        return body.GetProperty("threads").EnumerateArray().Select(t => new DebugThread(t.GetProperty("id").GetInt32(), t.GetProperty("name").GetString() ?? string.Empty)).ToList();
    }

    public async Task<IReadOnlyList<StackFrameInfo>> StackTraceAsync(int threadId, CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync("stackTrace", new { threadId, startFrame = 0, levels = 100 }, cancellationToken).ConfigureAwait(false);
        return body.GetProperty("stackFrames").EnumerateArray().Select(f => new StackFrameInfo(
            f.GetProperty("id").GetInt32(),
            f.GetProperty("name").GetString() ?? string.Empty,
            f.TryGetProperty("source", out var source) && source.TryGetProperty("path", out var path) ? path.GetString() : null,
            f.TryGetProperty("line", out var line) ? line.GetInt32() : 0,
            f.TryGetProperty("column", out var column) ? column.GetInt32() : 0)).ToList();
    }

    public async Task<IReadOnlyList<ScopeInfo>> ScopesAsync(int frameId, CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync("scopes", new { frameId }, cancellationToken).ConfigureAwait(false);
        return body.GetProperty("scopes").EnumerateArray().Select(s => new ScopeInfo(s.GetProperty("name").GetString() ?? string.Empty, s.GetProperty("variablesReference").GetInt32())).ToList();
    }

    public async Task<IReadOnlyList<VariableInfo>> VariablesAsync(int variablesReference, CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync("variables", new { variablesReference }, cancellationToken).ConfigureAwait(false);
        return body.GetProperty("variables").EnumerateArray().Select(v => new VariableInfo(
            v.GetProperty("name").GetString() ?? string.Empty,
            v.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty,
            v.TryGetProperty("type", out var type) ? type.GetString() : null,
            v.TryGetProperty("variablesReference", out var reference) ? reference.GetInt32() : 0)).ToList();
    }

    /// <param name="context">"watch", "hover" or "repl".</param>
    public async Task<VariableInfo> EvaluateAsync(string expression, int? frameId, string context, CancellationToken cancellationToken)
    {
        var body = await _connection.RequestAsync("evaluate", new { expression, frameId, context }, cancellationToken).ConfigureAwait(false);
        return new VariableInfo(
            expression,
            body.GetProperty("result").GetString() ?? string.Empty,
            body.TryGetProperty("type", out var type) ? type.GetString() : null,
            body.TryGetProperty("variablesReference", out var reference) ? reference.GetInt32() : 0);
    }

    public Task ContinueAsync(int threadId, CancellationToken cancellationToken) => RunAsync("continue", threadId, cancellationToken);

    public Task NextAsync(int threadId, CancellationToken cancellationToken) => RunAsync("next", threadId, cancellationToken);

    public Task StepInAsync(int threadId, CancellationToken cancellationToken) => RunAsync("stepIn", threadId, cancellationToken);

    public Task StepOutAsync(int threadId, CancellationToken cancellationToken) => RunAsync("stepOut", threadId, cancellationToken);

    public Task PauseAsync(int threadId, CancellationToken cancellationToken) => _connection.RequestAsync("pause", new { threadId }, cancellationToken);

    /// <summary>Ends the session and the debuggee.</summary>
    public async Task StopAsync()
    {
        try
        {
            await _connection.RequestAsync("disconnect", new { terminateDebuggee = true }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DapException or TimeoutException or OperationCanceledException or IOException)
        {
            // Adapter already gone or not answering; the process is killed below.
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsRunning = false;
        await _connection.DisposeAsync().ConfigureAwait(false);
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        _process.Dispose();
    }

    private async Task RunAsync(string command, int threadId, CancellationToken cancellationToken)
    {
        IsRunning = true;
        Continued?.Invoke(this, EventArgs.Empty);
        await _connection.RequestAsync(command, new { threadId }, cancellationToken).ConfigureAwait(false);
    }

    private void OnEvent(string name, JsonElement body)
    {
        switch (name)
        {
            case "initialized":
                _initialized.TrySetResult();
                break;
            case "stopped":
                IsRunning = false;
                Stopped?.Invoke(this, new StoppedInfo(
                    body.TryGetProperty("reason", out var reason) ? reason.GetString() ?? string.Empty : string.Empty,
                    body.TryGetProperty("threadId", out var thread) ? thread.GetInt32() : 0,
                    body.TryGetProperty("description", out var description) ? description.GetString() : null));
                break;
            case "continued":
                IsRunning = true;
                Continued?.Invoke(this, EventArgs.Empty);
                break;
            case "output":
                Output?.Invoke(this, new OutputInfo(body.TryGetProperty("category", out var category) ? category.GetString() ?? "console" : "console", body.TryGetProperty("output", out var output) ? output.GetString() ?? string.Empty : string.Empty));
                break;
            case "exited":
                Exited?.Invoke(this, body.TryGetProperty("exitCode", out var code) ? code.GetInt32() : 0);
                break;
            case "terminated":
                IsRunning = false;
                Terminated?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
