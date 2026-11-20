using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VsLike.Debugging.Netcoredbg;

/// <summary>
/// netcoredbg (MIT, Samsung) als Debug-Adapter. Gesucht wird: Umgebungsvariable <c>VSLIKE_NETCOREDBG</c>, neben der App
/// (Paket), im Cache. Fehlt er, wird die gepinnte Version von GitHub geladen und per SHA-256 geprüft.
/// osx-x64 gibt es nur bis 3.1.3 (siehe docs/DECISIONS.md).
/// </summary>
public static class NetcoredbgPackage
{
    public const string EnvironmentVariable = "VSLIKE_NETCOREDBG";

    private static readonly IReadOnlyDictionary<string, (string Version, string Asset, string Sha256)> Builds = new Dictionary<string, (string, string, string)>
    {
        ["osx-arm64"] = ("3.2.0-1092", "netcoredbg-osx-arm64.zip", "f4fa33b3ff874910cc184b4bb3b9c56d0abdf5c6521cee0b144d7c6e4a6e59ea"),
        ["osx-x64"] = ("3.1.3-1062", "netcoredbg-osx-amd64.tar.gz", "49459b066836b6a452f418501d7ecab57bcd7e60d8464faac21ff70b496b8634"),
        ["linux-x64"] = ("3.2.0-1092", "netcoredbg-linux-amd64.tar.gz", "080eb3b2d2152465f599d3b33d1ee6e747794e11cc0a3773ec689f5e5f2c5afa"),
        ["linux-arm64"] = ("3.2.0-1092", "netcoredbg-linux-arm64.tar.gz", "065ff49badec8a695dbea2de6ab6a330c774a191e426a217ab8cc05250627ccb"),
    };

    public static string RuntimeId
    {
        get
        {
            var os = OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return $"{os}-{arch}";
        }
    }

    public static bool IsSupported => Builds.ContainsKey(RuntimeId);

    public static string Version => Builds.TryGetValue(RuntimeId, out var build) ? build.Version : string.Empty;

    public static string CacheDirectory
    {
        get
        {
            var root = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "VsLike")
                : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg ? Path.Combine(xdg, "vslike") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "vslike");
            return Path.Combine(root, "netcoredbg", Version);
        }
    }

    public static string DownloadUrl(string runtimeId) => $"https://github.com/Samsung/netcoredbg/releases/download/{Builds[runtimeId].Version}/{Builds[runtimeId].Asset}";

    public static string Sha256(string runtimeId) => Builds[runtimeId].Sha256;

    /// <summary>Returns the netcoredbg executable if present (environment, bundled, cache), otherwise null.</summary>
    public static string? Locate()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            Path.Combine(AppContext.BaseDirectory, "netcoredbg", "netcoredbg"),
            Path.Combine(CacheDirectory, "netcoredbg", "netcoredbg"),
        };
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    public static async Task<string> EnsureAsync(HttpClient http, CancellationToken cancellationToken)
    {
        if (Locate() is { } existing)
        {
            return existing;
        }

        if (!IsSupported)
        {
            throw new PlatformNotSupportedException($"netcoredbg is not available for {RuntimeId}.");
        }

        var url = DownloadUrl(RuntimeId);
        var bytes = await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash != Sha256(RuntimeId))
        {
            throw new InvalidDataException($"Checksum mismatch for {url}: {hash}");
        }

        var target = CacheDirectory;
        var staging = target + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        using (var archive = new MemoryStream(bytes))
        {
            if (url.EndsWith(".zip", StringComparison.Ordinal))
            {
                ZipFile.ExtractToDirectory(archive, staging);
            }
            else
            {
                await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
            }
        }

        var executable = Path.Combine(staging, "netcoredbg", "netcoredbg");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(staging, target);
        return Path.Combine(target, "netcoredbg", "netcoredbg");
    }
}
