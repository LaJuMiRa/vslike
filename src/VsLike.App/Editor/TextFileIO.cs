using System.Text;

namespace VsLike.App.Editor;

/// <summary>Liest und schreibt Textdateien unter Beibehaltung von Kodierung (inkl. BOM).</summary>
public static class TextFileIO
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task<(string Text, Encoding Encoding)> ReadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var encoding = DetectEncoding(bytes);
        var preamble = encoding.GetPreamble().Length;
        return (encoding.GetString(bytes, preamble, bytes.Length - preamble), encoding);
    }

    public static async Task WriteAsync(string path, string text, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".vslike-tmp";
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(preamble).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static Encoding DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode;
        }

        return Utf8NoBom;
    }
}
