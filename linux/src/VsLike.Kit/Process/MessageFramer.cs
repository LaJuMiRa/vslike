using System.Text;

namespace VsLike.Kit;

/// <summary>
/// Splits a byte stream into <c>Content-Length</c> framed messages (the framing of JSON-RPC in the engine protocol and of
/// the Debug Adapter Protocol). Feed it chunks as they arrive; it returns every complete message body. Not thread-safe.
/// </summary>
public sealed class MessageFramer
{
    private static readonly byte[] Separator = "\r\n\r\n"u8.ToArray();
    private readonly List<byte> _buffer = [];

    public static byte[] Frame(byte[] body)
    {
        var header = Encoding.UTF8.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> chunk)
    {
        _buffer.AddRange(chunk.ToArray());
        var messages = new List<byte[]>();
        while (NextMessage() is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }

    private byte[]? NextMessage()
    {
        while (true)
        {
            var headerEnd = IndexOf(Separator);
            if (headerEnd < 0)
            {
                return null;
            }

            var header = Encoding.UTF8.GetString([.. _buffer.GetRange(0, headerEnd)]);
            int? length = null;
            foreach (var line in header.Split("\r\n"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1].Trim(), out var parsed))
                {
                    length = parsed;
                }
            }

            var bodyStart = headerEnd + Separator.Length;
            if (length is null)
            {
                // Malformed header: drop it so that the stream can recover at the next header.
                _buffer.RemoveRange(0, bodyStart);
                continue;
            }

            if (_buffer.Count - bodyStart < length.Value)
            {
                return null;
            }

            var body = _buffer.GetRange(bodyStart, length.Value).ToArray();
            _buffer.RemoveRange(0, bodyStart + length.Value);
            return body;
        }
    }

    private int IndexOf(byte[] pattern)
    {
        for (var i = 0; i <= _buffer.Count - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (_buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
