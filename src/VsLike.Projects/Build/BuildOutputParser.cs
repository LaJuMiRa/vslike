using System.Text.RegularExpressions;
using VsLike.Core.Build;
using VsLike.Core.Code;

namespace VsLike.Projects.Build;

/// <summary>
/// Liest Diagnosen im kanonischen MSBuild-Format aus der Build-Ausgabe, z. B.
/// <c>/src/Program.cs(12,5): error CS1002: ; expected [/src/App.csproj]</c> oder <c>App.csproj : warning NU1603: …</c>.
/// Die Schlüsselwörter <c>error</c>/<c>warning</c> sind auch in lokalisierter Ausgabe englisch.
/// </summary>
public static partial class BuildOutputParser
{
    public static bool TryParse(string line, out DiagnosticInfo? diagnostic)
    {
        diagnostic = null;
        var match = CanonicalPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var origin = match.Groups["origin"].Value.Trim();
        var severity = match.Groups["severity"].Value.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverityKind.Error,
            "warning" => DiagnosticSeverityKind.Warning,
            _ => DiagnosticSeverityKind.Info,
        };
        var project = match.Groups["project"].Success ? match.Groups["project"].Value : null;
        var hasPosition = match.Groups["line"].Success;
        string? file = origin.Length > 0 && origin != "MSBUILD" && Path.IsPathRooted(origin) ? origin : null;
        file ??= project is not null && origin.Length > 0 && origin != "MSBUILD" ? Path.Combine(Path.GetDirectoryName(project)!, origin) : null;
        diagnostic = new DiagnosticInfo(
            Id: match.Groups["code"].Value,
            Message: match.Groups["message"].Value.Trim(),
            Severity: severity,
            FilePath: file,
            Start: 0,
            Length: 0,
            StartLine: hasPosition ? int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture) : 1,
            StartColumn: match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture) : 1,
            Category: "Build",
            ProjectName: project is null ? null : Path.GetFileNameWithoutExtension(project));
        return true;
    }

    /// <summary>All distinct diagnostics of a build (MSBuild repeats them in its summary).</summary>
    public static IReadOnlyList<DiagnosticInfo> Parse(IEnumerable<OutputLine> lines)
    {
        var result = new List<DiagnosticInfo>();
        var seen = new HashSet<(string, string?, int, int, string)>();
        foreach (var line in lines)
        {
            if (TryParse(line.Text, out var diagnostic) && seen.Add((diagnostic!.Id, diagnostic.FilePath, diagnostic.StartLine, diagnostic.StartColumn, diagnostic.Message)))
            {
                result.Add(diagnostic);
            }
        }

        return result;
    }

    [GeneratedRegex(@"^\s*(?<origin>[^\s(][^(]*?|)(\((?<line>\d+)(,(?<column>\d+))?(,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning|info|message)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(\s+\[(?<project>[^\]]+)\])?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPattern();
}
