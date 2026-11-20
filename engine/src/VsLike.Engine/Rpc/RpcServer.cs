using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VsLike.Engine.Rpc;

/// <summary>
/// Dispatches incoming JSON-RPC requests to registered handlers. Requests run concurrently on the thread pool; a
/// <c>$/cancelRequest</c> notification cancels the matching request. Notifications (no id) get no response.
/// </summary>
public sealed class RpcServer
{
    private readonly RpcConnection _connection;
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private readonly TextWriter _log;

    public RpcServer(RpcConnection connection, TextWriter log)
    {
        _connection = connection;
        _log = log;
    }

    public RpcConnection Connection => _connection;

    public void On<TParams>(string method, Func<TParams, CancellationToken, Task<object?>> handler)
        => _handlers[method] = (parameters, cancellationToken) => handler(Deserialize<TParams>(parameters), cancellationToken);

    public void On(string method, Func<CancellationToken, Task<object?>> handler)
        => _handlers[method] = (_, cancellationToken) => handler(cancellationToken);

    /// <summary>Serves until the input ends or a handler requests shutdown via <paramref name="stop"/>.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        var pending = new List<Task>();
        while (!stop.IsCancellationRequested)
        {
            JsonObject? message;
            try
            {
                message = await _connection.ReadAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message is null)
            {
                break;
            }

            // Notifications (document text updates, cancellations) run inline so that they keep their order and a
            // request that follows a text update always sees the new text.
            if (message["id"] is null)
            {
                await HandleAsync(message).ConfigureAwait(false);
                continue;
            }

            pending.RemoveAll(t => t.IsCompleted);
            pending.Add(Task.Run(() => HandleAsync(message), CancellationToken.None));
        }

        foreach (var source in _running.Values)
        {
            await source.CancelAsync().ConfigureAwait(false);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    private static TParams Deserialize<TParams>(JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new RpcException(RpcException.InvalidParams, "Missing params.");
        }

        try
        {
            return parameters.Deserialize<TParams>(RpcJson.Options) ?? throw new RpcException(RpcException.InvalidParams, "Empty params.");
        }
        catch (JsonException ex)
        {
            throw new RpcException(RpcException.InvalidParams, ex.Message);
        }
    }

    private static JsonObject Error(int code, string message)
        => new() { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private async Task HandleAsync(JsonObject message)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"]?.DeepClone();
        var parameters = message["params"] is { } node ? JsonSerializer.SerializeToElement(node) : default;
        if (method is null)
        {
            return;
        }

        if (method == "$/cancelRequest")
        {
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("id", out var cancelId) && _running.TryGetValue(cancelId.ToString(), out var source))
            {
                await source.CancelAsync().ConfigureAwait(false);
            }

            return;
        }

        var key = id?.ToJsonString() ?? string.Empty;
        using var cancellation = new CancellationTokenSource();
        if (id is not null)
        {
            _running[key] = cancellation;
        }

        JsonObject response;
        try
        {
            if (!_handlers.TryGetValue(method, out var handler))
            {
                throw new RpcException(RpcException.MethodNotFound, $"Unknown method '{method}'.");
            }

            var result = await handler(parameters, cancellation.Token).ConfigureAwait(false);
            response = new JsonObject { ["result"] = JsonSerializer.SerializeToNode(result, RpcJson.Options) };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            response = Error(RpcException.RequestCancelled, "Request cancelled.");
        }
        catch (RpcException ex)
        {
            response = Error(ex.Code, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            response = Error(RpcException.OperationFailed, ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await _log.WriteLineAsync($"[engine] {method} failed: {ex}").ConfigureAwait(false);
            response = Error(RpcException.InternalError, ex.Message);
        }
        finally
        {
            _running.TryRemove(key, out _);
        }

        if (id is null)
        {
            return;
        }

        response["jsonrpc"] = "2.0";
        response["id"] = id;
        await _connection.WriteAsync(response, CancellationToken.None).ConfigureAwait(false);
    }
}
