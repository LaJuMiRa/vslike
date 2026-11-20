using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VsLike.Engine.Rpc;

/// <summary>
/// JSON-RPC 2.0 messages with <c>Content-Length</c> framing (the framing of the Language Server Protocol) over a pair
/// of streams. Writes are serialised; reads happen on one loop.
/// </summary>
public sealed class RpcConnection
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public RpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Reads the next message, or returns null at the end of the input stream.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken)
    {
        var length = -1;
        while (true)
        {
            var line = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            const string Header = "Content-Length:";
            if (line.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line[Header.Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (length < 0)
        {
            throw new InvalidDataException("Message without Content-Length header.");
        }

        var buffer = new byte[length];
        await _input.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(buffer) as JsonObject ?? throw new InvalidDataException("Message is not a JSON object.");
    }

    public async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(RpcJson.Options));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        => WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = JsonSerializer.SerializeToNode(parameters, RpcJson.Options),
            },
            cancellationToken);

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var single = new byte[1];
        while (true)
        {
            var read = await _input.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : throw new EndOfStreamException("Input ended inside a header.");
            }

            if (single[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            }

            bytes.Add(single[0]);
        }
    }
}
