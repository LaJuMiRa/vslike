using System.Text.Json;
using System.Text.Json.Serialization;

namespace VsLike.Core.Settings;

/// <summary>
/// Liest und schreibt ein JSON-Dokument atomar (Temp-Datei + Move). Defekte Dateien werden ignoriert und gesichert.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public JsonFileStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public T Load()
    {
        if (!File.Exists(Path))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(Path), Options) ?? new T();
        }
        catch (JsonException)
        {
            File.Copy(Path, Path + ".broken", overwrite: true);
            return new T();
        }
    }

    public void Save(T value)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
