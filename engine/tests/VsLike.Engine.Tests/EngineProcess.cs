using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VsLike.Core.Build;
using VsLike.Engine.Rpc;

namespace VsLike.Engine.Tests;

/// <summary>Starts the built engine (<c>dotnet VsLikeEngine.dll</c>) as a child process and talks JSON-RPC to it.</summary>
public sealed class EngineProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly RpcConnection _connection;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly Task _reader;
    private int _nextId;

    private EngineProcess(Process process)
    {
        _process = process;
        _connection = new RpcConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _reader = Task.Run(ReadLoopAsync);
    }

    public ConcurrentQueue<JsonObject> Notifications { get; } = new();

    public static EngineProcess Start()
    {
        // Clean environment like the app started from Finder: no MSBuild variables of this test host.
        var start = new DotnetCli().CreateStartInfo([Path.Combine(AppContext.BaseDirectory, "VsLikeEngine.dll")], workingDirectory: null);
        start.RedirectStandardInput = true;
        var process = Process.Start(start)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new EngineProcess(process);
    }

    public async Task<JsonNode?> RequestAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        var response = await RawRequestAsync(method, parameters, timeout).ConfigureAwait(false);
        if (response["error"] is { } error)
        {
            throw new InvalidOperationException($"{method}: {error["code"]} {error["message"]}");
        }

        return response["result"];
    }

    public async Task<T> RequestAsync<T>(string method, object? parameters = null, TimeSpan? timeout = null)
        => (await RequestAsync(method, parameters, timeout).ConfigureAwait(false)).Deserialize<T>(RpcJson.Options)!;

    public async Task<JsonObject> RawRequestAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        await _connection.WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = JsonSerializer.SerializeToNode(parameters, RpcJson.Options),
            },
            CancellationToken.None).ConfigureAwait(false);
        return await completion.Task.WaitAsync(timeout ?? TimeSpan.FromMinutes(2)).ConfigureAwait(false);
    }

    public Task NotifyAsync(string method, object parameters) => _connection.NotifyAsync(method, parameters, CancellationToken.None);

    /// <summary>Waits until a notification with the method matches the predicate.</summary>
    public async Task<JsonObject> WaitForNotificationAsync(string method, Func<JsonObject, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            while (Notifications.TryDequeue(out var notification))
            {
                if (notification["method"]?.GetValue<string>() == method && predicate(notification))
                {
                    return notification;
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"No '{method}' notification within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RequestAsync("shutdown", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _reader.ConfigureAwait(false);
        _process.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _connection.ReadAsync(CancellationToken.None).ConfigureAwait(false) is { } message)
            {
                if (message["id"] is { } id && message["method"] is null)
                {
                    if (_pending.TryRemove(id.GetValue<int>(), out var completion))
                    {
                        completion.TrySetResult(message);
                    }
                }
                else
                {
                    Notifications.Enqueue(message);
                }
            }
        }
        catch (IOException)
        {
        }
    }
}
