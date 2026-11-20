using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VsLike.Debugging.Dap;

/// <summary>
/// Debug Adapter Protocol über zwei Streams: Nachrichten mit <c>Content-Length</c>-Kopf, Requests mit
/// Sequenznummern, Antworten werden ihrem Request zugeordnet, Events gehen an <see cref="EventReceived"/>.
/// </summary>
public sealed class DapConnection : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _reader;
    private int _sequence;

    /// <param name="input">Stream the adapter writes to (its stdout).</param>
    /// <param name="output">Stream the adapter reads from (its stdin).</param>
    public DapConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Raised on a background thread for every DAP event (name, body).</summary>
    public event EventHandler<(string Event, JsonElement Body)>? EventReceived;

    /// <summary>Raised when the adapter closed its output.</summary>
    public event EventHandler? Closed;

    public void Start() => _reader = Task.Run(ReadLoopAsync);

    /// <summary>Sends a request and returns the response body; throws <see cref="DapException"/> on failure.</summary>
    public async Task<JsonElement> RequestAsync(string command, object? arguments, CancellationToken cancellationToken)
    {
        var seq = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = completion;
        var message = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
        };
        if (arguments is not null)
        {
            message["arguments"] = JsonSerializer.SerializeToNode(arguments, DapJson.Options);
        }

        await WriteAsync(message, cancellationToken).ConfigureAwait(false);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        foreach (var pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }

        if (_reader is not null)
        {
            try
            {
                await _reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The adapter process is gone; nothing left to read.
            }
        }

        _stop.Dispose();
        _writeLock.Dispose();
    }

    internal static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = -1;
        while (true)
        {
            var line = await ReadHeaderLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (length < 0)
        {
            return null;
        }

        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer);
    }

    internal static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }

    private static async Task<string?> ReadHeaderLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            }

            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            }

            bytes.Add(one[0]);
        }
    }

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var frame = Frame(message.ToJsonString());
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var json = await ReadMessageAsync(_input, _stop.Token).ConfigureAwait(false);
                if (json is null)
                {
                    break;
                }

                Dispatch(json);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or EndOfStreamException)
        {
            // Adapter exited.
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new DapException("Debug adapter closed the connection."));
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Dispatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();
        switch (root.GetProperty("type").GetString())
        {
            case "response":
                var requestSeq = root.GetProperty("request_seq").GetInt32();
                if (_pending.TryRemove(requestSeq, out var completion))
                {
                    if (root.TryGetProperty("success", out var success) && success.GetBoolean())
                    {
                        completion.TrySetResult(root.TryGetProperty("body", out var body) ? body : default);
                    }
                    else
                    {
                        var text = root.TryGetProperty("message", out var message) ? message.GetString() : null;
                        if (root.TryGetProperty("body", out var errorBody) && errorBody.ValueKind == JsonValueKind.Object && errorBody.TryGetProperty("error", out var error) && error.TryGetProperty("format", out var format))
                        {
                            text = format.GetString();
                        }

                        completion.TrySetException(new DapException(text ?? root.GetProperty("command").GetString() + " failed"));
                    }
                }

                break;
            case "event":
                EventReceived?.Invoke(this, (root.GetProperty("event").GetString()!, root.TryGetProperty("body", out var eventBody) ? eventBody : default));
                break;
            case "request":
                _ = WriteAsync(
                    new JsonObject
                    {
                        ["seq"] = Interlocked.Increment(ref _sequence),
                        ["type"] = "response",
                        ["request_seq"] = root.GetProperty("seq").GetInt32(),
                        ["command"] = root.GetProperty("command").GetString(),
                        ["success"] = false,
                        ["message"] = "not supported",
                    },
                    CancellationToken.None);
                break;
        }
    }
}
