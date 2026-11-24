using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace VsLike.Kit;

public class EngineException : Exception
{
    public EngineException(string message, int code = -1)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Gets the JSON-RPC error code (-1 for local errors).</summary>
    public int Code { get; }

    /// <summary>Gets a value indicating whether the engine reported a handled failure (e.g. "type not found") with code 1.</summary>
    public bool IsOperationFailure => this is EngineRemoteException && Code == 1;

    public static EngineException NotRunning() => new EngineNotRunningException();

    public static EngineException EngineNotFound(string path) => new($"The analysis engine was not found at {path}.");

    public static EngineException Remote(int code, string message) => new EngineRemoteException(code, message);

    public static EngineException InvalidResponse(string detail) => new($"Invalid response from the analysis engine: {detail}");
}

public sealed class EngineNotRunningException() : EngineException("The analysis engine is not running.");

public sealed class EngineRemoteException(int code, string message) : EngineException(message, code);

/// <summary>A notification pushed by the engine, e.g. <c>workspace/diagnostics</c>.</summary>
public sealed record EngineNotification(string Method, JsonNode? Params);

/// <summary>
/// JSON-RPC client of the <c>VsLikeEngine</c> process (Roslyn, StyleCop, diagram writer). Requests are async and
/// cancellable; notifications arrive on <see cref="Notifications"/>. The process is started with the installed <c>dotnet</c>.
/// </summary>
public sealed partial class EngineClient : IDisposable
{
    private readonly string _engineDll;
    private readonly CommandRunner _runner;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<EngineNotification> _notifications = Channel.CreateUnbounded<EngineNotification>();
    private readonly MessageFramer _framer = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _pending = [];
    private readonly List<string> _stderrTail = [];
    private Process? _process;
    private Stream? _input;
    private int _nextId;
    private Action<int>? _onExit;

    public EngineClient(string engineDll, CommandRunner runner)
    {
        _engineDll = engineDll;
        _runner = runner;
    }

    public ChannelReader<EngineNotification> Notifications => _notifications.Reader;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>Gets the last lines the engine wrote to stderr (for error reports).</summary>
    public string RecentLog
    {
        get
        {
            lock (_lock)
            {
                return string.Join("\n", _stderrTail);
            }
        }
    }

    /// <summary>Starts the process. <paramref name="onExit"/> is called when it ends unexpectedly or after <see cref="Stop"/>.</summary>
    public void Start(Action<int>? onExit = null)
    {
        if (!File.Exists(_engineDll))
        {
            throw EngineException.EngineNotFound(_engineDll);
        }

        var info = new ProcessStartInfo(_runner.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add(_engineDll);
        info.Environment.Clear();
        foreach (var (key, value) in _runner.ChildEnvironment())
        {
            info.Environment[key] = value;
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += (_, _) => Terminated(process);
        process.Start();
        lock (_lock)
        {
            _onExit = onExit;
            _process = process;
            _input = process.StandardInput.BaseStream;
        }

        _ = Task.Run(() => ReadLoop(process.StandardOutput.BaseStream));
        _ = Task.Run(() => LogLoop(process.StandardError));
    }

    public async Task Stop()
    {
        if (IsRunning)
        {
            try
            {
                await RawRequest("shutdown", null, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is EngineException or OperationCanceledException or IOException)
            {
                // The engine may already be gone.
            }
        }

        Process? running;
        lock (_lock)
        {
            running = _process;
        }

        if (running is { HasExited: false })
        {
            CommandRunner.TryKill(running);
        }
    }

    public void Dispose()
    {
        Process? running;
        lock (_lock)
        {
            running = _process;
        }

        if (running is not null)
        {
            CommandRunner.TryKill(running);
            running.Dispose();
        }

        _writeLock.Dispose();
    }

    /// <summary>Sends a request and decodes its <c>result</c>.</summary>
    public async Task<T> Request<T>(string method, object? parameters, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await RawRequest(method, JSONValue.Encode(parameters), timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            return result is null ? default! : result.Deserialize<T>(JSONValue.Options)!;
        }
        catch (JsonException ex)
        {
            throw EngineException.InvalidResponse($"{method}: {ex.Message}");
        }
    }

    public async Task<JsonNode?> RawRequest(string method, JsonNode? parameters, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        int id;
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Stream? input;
        lock (_lock)
        {
            id = ++_nextId;
            input = _process is { HasExited: false } ? _input : null;
            if (input is not null)
            {
                _pending[id] = completion;
            }
        }

        if (input is null)
        {
            throw EngineException.NotRunning();
        }

        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        using var timeoutSource = timeout is { } t ? new CancellationTokenSource(t) : null;
        await using var timeoutRegistration = timeoutSource?.Token.Register(() =>
            Fail(id, EngineException.Remote(-32800, $"{method} timed out after {(int)timeout!.Value.TotalSeconds} s."))) ?? default;
        await using var cancelRegistration = cancellationToken.Register(() =>
        {
            Notify("$/cancelRequest", new JsonObject { ["id"] = id });
            Fail(id, new OperationCanceledException(cancellationToken));
        });
        await Write(message, input).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Sends a notification (no response). Notifications keep their order in the engine.</summary>
    public void Notify(string method, JsonNode? parameters)
    {
        Stream? input;
        lock (_lock)
        {
            input = _input;
        }

        if (input is null)
        {
            return;
        }

        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters };
        _ = Write(message, input);
    }

    private async Task Write(JsonObject message, Stream input)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(JSONValue.Options));
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await input.WriteAsync(MessageFramer.Frame(body)).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process ended; Terminated fails the pending requests.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Fail(int id, Exception error)
    {
        TaskCompletionSource<JsonNode?>? completion;
        lock (_lock)
        {
            _pending.Remove(id, out completion);
        }

        completion?.TrySetException(error);
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
            // Process ended.
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

            Dispatch(message);
        }
    }

    private void Dispatch(JsonNode? message)
    {
        var method = message.StringValue("method");
        if (message.IntValue("id") is { } id && method is null)
        {
            TaskCompletionSource<JsonNode?>? completion;
            lock (_lock)
            {
                _pending.Remove(id, out completion);
            }

            if (message.Child("error") is { } error)
            {
                completion?.TrySetException(EngineException.Remote(error.IntValue("code") ?? -1, error.StringValue("message") ?? "error"));
            }
            else
            {
                completion?.TrySetResult(message.Child("result")?.DeepClone());
            }
        }
        else if (method is not null)
        {
            _notifications.Writer.TryWrite(new EngineNotification(method, message.Child("params")?.DeepClone()));
        }
    }

    private async Task LogLoop(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_lock)
                {
                    _stderrTail.Add(line);
                    if (_stderrTail.Count > 200)
                    {
                        _stderrTail.RemoveRange(0, _stderrTail.Count - 200);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Process ended.
        }
    }

    private void Terminated(Process process)
    {
        List<TaskCompletionSource<JsonNode?>> waiting;
        Action<int>? callback;
        lock (_lock)
        {
            if (!ReferenceEquals(process, _process))
            {
                return;
            }

            waiting = [.. _pending.Values];
            _pending.Clear();
            _input = null;
            callback = _onExit;
        }

        foreach (var completion in waiting)
        {
            completion.TrySetException(EngineException.NotRunning());
        }

        callback?.Invoke(process.ExitCode);
    }
}
