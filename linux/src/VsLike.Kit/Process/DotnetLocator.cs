using System.Globalization;

namespace VsLike.Kit;

/// <summary>
/// Finds the <c>dotnet</c> host. Apps started from a desktop launcher may not get the shell's <c>PATH</c>, so the usual
/// Linux install locations (distribution packages and the install script) are checked explicitly.
/// </summary>
public static class DotnetLocator
{
    public const int MinimumMajorVersion = 10;

    public static string? Locate(IReadOnlyDictionary<string, string>? environment = null)
    {
        var env = environment ?? CurrentEnvironment();
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        if (env.TryGetValue("DOTNET_ROOT", out var root) && root.Length > 0)
        {
            candidates.Add(root + "/dotnet");
        }

        candidates.AddRange((env.GetValueOrDefault("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries).Select(p => p + "/dotnet"));
        candidates.AddRange(
        [
            "/usr/bin/dotnet",
            "/usr/lib64/dotnet/dotnet",
            "/usr/lib/dotnet/dotnet",
            "/usr/share/dotnet/dotnet",
            "/usr/local/bin/dotnet",
            home + "/.dotnet/dotnet",
        ]);
        return candidates.Select(ResolveSymlinks).FirstOrDefault(IsExecutable);
    }

    /// <summary>Parses <c>dotnet --list-sdks</c> (e.g. <c>10.0.402 [/usr/share/dotnet/sdk]</c>), newest first.</summary>
    public static IReadOnlyList<SdkInfo> ParseSdks(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split(' ', 2);
                var version = SemanticVersion.Parse(parts[0]);
                if (version is null)
                {
                    return null;
                }

                var path = parts.Length > 1 ? parts[1].Trim('[', ']', ' ', '\r') : string.Empty;
                return new SdkInfo(version, parts[0], path);
            })
            .OfType<SdkInfo>()
            .OrderByDescending(s => s.Version).ToList();

    /// <summary>One target framework (<c>net10.0</c>) per installed SDK major.minor, newest first, with the SDKs that provide it.</summary>
    public static IReadOnlyList<TargetFrameworkOption> TargetFrameworks(IEnumerable<SdkInfo> sdks)
    {
        var options = new List<TargetFrameworkOption>();
        foreach (var sdk in sdks.OrderByDescending(s => s.Version))
        {
            var moniker = string.Create(CultureInfo.InvariantCulture, $"net{sdk.Version.Major}.{sdk.Version.Minor}");
            var index = options.FindIndex(o => o.Moniker == moniker);
            if (index >= 0)
            {
                options[index] = options[index] with { SdkVersions = [.. options[index].SdkVersions, sdk.FullVersion] };
            }
            else
            {
                options.Add(new TargetFrameworkOption(moniker, [sdk.FullVersion]));
            }
        }

        return options;
    }

    public static async Task<IReadOnlyList<SdkInfo>> ListSdks(CommandRunner runner)
    {
        try
        {
            var result = await runner.Run(["--list-sdks"]).ConfigureAwait(false);
            return result.Succeeded ? ParseSdks(result.StandardOutput) : [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>A copy of the current process environment.</summary>
    public static Dictionary<string, string> CurrentEnvironmentCopy() => CurrentEnvironment();

    internal static Dictionary<string, string> CurrentEnvironment()
    {
        var result = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            result[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        return result;
    }

    private static string ResolveSymlinks(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.LinkTarget is not null ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
}

public sealed record SdkInfo(SemanticVersion Version, string FullVersion, string Path)
{
    public bool IsSupported => Version.Major >= DotnetLocator.MinimumMajorVersion;
}

/// <summary>A target framework moniker such as <c>net10.0</c> and the installed SDK versions that provide it (newest first).</summary>
public sealed record TargetFrameworkOption(string Moniker, IReadOnlyList<string> SdkVersions)
{
    public string Id => Moniker;

    public bool Equals(TargetFrameworkOption? other) =>
        other is not null && Moniker == other.Moniker && SdkVersions.SequenceEqual(other.SdkVersions);

    public override int GetHashCode() => Moniker.GetHashCode(StringComparison.Ordinal);
}

/// <summary><c>major.minor.patch</c> with an optional pre-release suffix (ignored for ordering of equal numbers).</summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static SemanticVersion? Parse(string text)
    {
        var main = text.Split('-', 2);
        var numbers = main[0].Split('.');
        if (numbers.Length < 2)
        {
            return null;
        }

        var parsed = new int[numbers.Length];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (!int.TryParse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i]))
            {
                return null;
            }
        }

        return new SemanticVersion(parsed[0], parsed[1], parsed.Length > 2 ? parsed[2] : 0, main.Length > 1 ? main[1] : null);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (numbers != 0)
        {
            return numbers;
        }

        // A release is newer than its pre-releases.
        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (l, r) => string.CompareOrdinal(l, r),
        };
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (PreRelease is null ? string.Empty : "-" + PreRelease);
}
