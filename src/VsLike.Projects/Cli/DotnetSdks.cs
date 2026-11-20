using System.Text.RegularExpressions;
using VsLike.Core.Build;

namespace VsLike.Projects.Cli;

/// <summary>Ermittelt die installierten .NET SDKs (Start-Prüfung der IDE).</summary>
public static partial class DotnetSdks
{
    public static readonly Version MinimumVersion = new(10, 0);

    public static IReadOnlyList<SdkInfo> Parse(string listSdksOutput)
        => LineRegex().Matches(listSdksOutput)
            .Select(m => (Match: m, Ok: Version.TryParse(m.Groups["v"].Value, out var v), Version: v))
            .Where(x => x.Ok)
            .Select(x => new SdkInfo(x.Version!, x.Match.Groups["full"].Value, x.Match.Groups["path"].Value))
            .OrderByDescending(s => s.Version)
            .ToList();

    public static async Task<IReadOnlyList<SdkInfo>> ListAsync(IDotnetCli cli, CancellationToken cancellationToken)
    {
        try
        {
            var result = await cli.RunAsync(["--list-sdks"], null, null, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? Parse(result.StandardOutput) : [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    public static bool HasSupportedSdk(IReadOnlyList<SdkInfo> sdks) => sdks.Any(s => s.Version >= MinimumVersion);

    [GeneratedRegex(@"^(?<full>(?<v>\d+\.\d+\.\d+)\S*)\s+\[(?<path>[^\]]+)\]", RegexOptions.Multiline)]
    private static partial Regex LineRegex();
}
