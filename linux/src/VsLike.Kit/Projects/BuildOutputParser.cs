using System.Globalization;
using System.Text.RegularExpressions;

namespace VsLike.Kit;

/// <summary>
/// Reads diagnostics in the canonical MSBuild format from build output, e.g.
/// <c>/src/Program.cs(12,5): error CS1002: ; expected [/src/App.csproj]</c> or <c>App.csproj : warning NU1603: …</c>.
/// The keywords <c>error</c>/<c>warning</c> stay English in localized output.
/// </summary>
public static partial class BuildOutputParser
{
    public static Diagnostic? Parse(string line)
    {
        var match = Pattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var origin = match.Groups["origin"].Value.Trim();
        var severity = match.Groups["severity"].Value.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info,
        };
        var project = match.Groups["project"].Success ? match.Groups["project"].Value : null;
        string? file = null;
        if (origin.Length > 0 && origin != "MSBUILD")
        {
            if (origin.StartsWith('/'))
            {
                file = origin;
            }
            else if (project is not null)
            {
                file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project) ?? "/", origin));
            }
        }

        return new Diagnostic(
            match.Groups["code"].Value,
            match.Groups["message"].Value.Trim(),
            severity,
            file,
            0,
            0,
            match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture) : 1,
            match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture) : 1,
            "Build",
            project is null ? null : Path.GetFileNameWithoutExtension(project));
    }

    /// <summary>All distinct diagnostics of a build (MSBuild repeats them in its summary).</summary>
    public static IReadOnlyList<Diagnostic> Parse(IEnumerable<OutputLine> lines)
    {
        var seen = new HashSet<string>();
        return [.. lines.Select(l => Parse(l.Text)).OfType<Diagnostic>().Where(d => seen.Add(d.Id))];
    }

    [GeneratedRegex(@"^\s*(?<origin>[^\s(][^(]*?|)(\((?<line>\d+)(,(?<column>\d+))?(,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning|info|message)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(\s+\[(?<project>[^\]]+)\])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
