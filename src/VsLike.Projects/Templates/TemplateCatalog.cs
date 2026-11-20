using VsLike.Core.Build;

namespace VsLike.Projects.Templates;

/// <summary>Liest die installierten C#-Projektvorlagen aus <c>dotnet new list</c> (Spalten über die Strichzeile).</summary>
public static class TemplateCatalog
{
    /// <summary>Templates offered first; others follow alphabetically.</summary>
    public static readonly IReadOnlyList<string> Preferred = ["console", "classlib", "xunit", "nunit", "mstest", "worker", "webapi", "web", "blazor"];

    private static readonly HashSet<string> WindowsOnly = new(StringComparer.OrdinalIgnoreCase) { "winforms", "winformslib", "winformscontrollib", "wpf", "wpflib", "wpfcustomcontrollib", "wpfusercontrollib" };

    public static IReadOnlyList<ProjectTemplate> Parse(string output)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var ruler = lines.FindIndex(l => l.StartsWith("---", StringComparison.Ordinal) && l.Contains("  ", StringComparison.Ordinal));
        if (ruler < 0)
        {
            return [];
        }

        var columns = ColumnRanges(lines[ruler]);
        if (columns.Count < 3)
        {
            return [];
        }

        var templates = new List<ProjectTemplate>();
        foreach (var line in lines.Skip(ruler + 1).Where(l => l.Trim().Length > 0))
        {
            string Cell(int index) => columns[index].Start >= line.Length ? string.Empty : line[columns[index].Start..Math.Min(line.Length, columns[index].End)].Trim();
            var shortName = Cell(1).Split(',')[0].Trim();
            if (shortName.Length == 0 || WindowsOnly.Contains(shortName))
            {
                continue;
            }

            templates.Add(new ProjectTemplate(Cell(0), shortName, columns.Count > 3 ? Cell(3) : string.Empty));
        }

        return [.. templates.OrderBy(t => Preferred.Contains(t.ShortName) ? Preferred.ToList().IndexOf(t.ShortName) : int.MaxValue).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static async Task<IReadOnlyList<ProjectTemplate>> ListAsync(IDotnetCli cli, CancellationToken cancellationToken)
    {
        var result = await cli.RunAsync(["new", "list", "--type", "project", "--language", "C#"], null, null, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? Parse(result.StandardOutput) : [];
    }

    private static List<(int Start, int End)> ColumnRanges(string ruler)
    {
        var ranges = new List<(int Start, int End)>();
        var start = -1;
        for (var i = 0; i <= ruler.Length; i++)
        {
            var dash = i < ruler.Length && ruler[i] == '-';
            if (dash && start < 0)
            {
                start = i;
            }
            else if (!dash && start >= 0)
            {
                ranges.Add((start, i));
                start = -1;
            }
        }

        // Letzte Spalte darf über die Strichlänge hinausgehen.
        if (ranges.Count > 0)
        {
            ranges[^1] = (ranges[^1].Start, int.MaxValue);
        }

        return ranges;
    }
}
