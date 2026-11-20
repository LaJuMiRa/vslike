using System.Text;
using System.Text.RegularExpressions;

namespace VsLike.Editor.Snippets;

/// <summary>Expandiert Snippet-Vorlagen: Einrückung je Zeile, Platzhalter und Variablen.</summary>
public static partial class SnippetExpander
{
    public static ExpandedSnippet Expand(string body, string indentation, string? typeName, string indentUnit = "    ")
    {
        var builder = new StringBuilder();
        var placeholders = new List<SnippetPlaceholder>();
        var finalCaret = -1;
        var source = body.Replace("$TYPE", typeName ?? "MyType", StringComparison.Ordinal).Replace("\t", indentUnit, StringComparison.Ordinal);
        var lines = source.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
            {
                builder.Append('\n').Append(indentation);
            }

            var line = lines[lineIndex];
            var position = 0;
            foreach (Match match in TokenRegex().Matches(line))
            {
                builder.Append(line, position, match.Index - position);
                var index = int.Parse(match.Groups["n1"].Success ? match.Groups["n1"].Value : match.Groups["n2"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var text = match.Groups["text"].Value;
                if (index == 0)
                {
                    finalCaret = builder.Length;
                }
                else
                {
                    placeholders.Add(new SnippetPlaceholder(index, builder.Length, text.Length));
                    builder.Append(text);
                }

                position = match.Index + match.Length;
            }

            builder.Append(line, position, line.Length - position);
        }

        // Verknüpfte Platzhalter (gleiche Nummer) übernehmen den Standardtext des ersten Vorkommens.
        return new ExpandedSnippet(builder.ToString(), [.. placeholders.OrderBy(p => p.Index).ThenBy(p => p.Start)], finalCaret < 0 ? builder.Length : finalCaret);
    }

    [GeneratedRegex(@"\$\{(?<n1>\d+)(?::(?<text>[^}]*))?\}|\$(?<n2>\d+)")]
    private static partial Regex TokenRegex();
}
