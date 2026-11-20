using System.Text.RegularExpressions;

namespace VsLike.Core.Search;

/// <summary>Suchen und Ersetzen in Text (Datei- und Projektsuche nutzen dieselbe Logik).</summary>
public static class TextSearch
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Builds the regex for the options; throws <see cref="ArgumentException"/> for an invalid pattern.</summary>
    public static Regex CreateRegex(SearchOptions options)
    {
        var pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
        if (options.WholeWord)
        {
            pattern = $@"\b(?:{pattern})\b";
        }

        var flags = RegexOptions.Multiline | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex(pattern, flags, RegexTimeout);
    }

    public static IReadOnlyList<TextMatch> FindAll(string text, SearchOptions options, int maxResults = 10_000)
    {
        if (string.IsNullOrEmpty(options.Query))
        {
            return [];
        }

        var regex = CreateRegex(options);
        var lineStarts = ComputeLineStarts(text);
        var results = new List<TextMatch>();
        foreach (Match match in regex.Matches(text))
        {
            if (match.Length == 0)
            {
                continue;
            }

            var lineIndex = FindLine(lineStarts, match.Index);
            var lineStart = lineStarts[lineIndex];
            var lineEnd = lineIndex + 1 < lineStarts.Count ? lineStarts[lineIndex + 1] : text.Length;
            var lineText = text[lineStart..lineEnd].TrimEnd('\r', '\n');
            results.Add(new TextMatch(match.Index, match.Length, lineIndex + 1, match.Index - lineStart + 1, lineText));
            if (results.Count >= maxResults)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Replaces all matches; with regex enabled, <paramref name="replacement"/> may use $1 etc.</summary>
    public static (string Text, int Count) ReplaceAll(string text, SearchOptions options, string replacement)
    {
        if (string.IsNullOrEmpty(options.Query))
        {
            return (text, 0);
        }

        var regex = CreateRegex(options);
        var count = 0;
        var result = regex.Replace(text, m =>
        {
            count++;
            return options.UseRegex ? m.Result(replacement) : replacement;
        });
        return (result, count);
    }

    /// <summary>Searches files in parallel; binary and unreadable files are skipped.</summary>
    public static async Task<IReadOnlyList<FileMatch>> SearchFilesAsync(IEnumerable<string> files, SearchOptions options, CancellationToken cancellationToken, int maxResults = 5_000)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<FileMatch>();
        await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            if (text.Contains('\0', StringComparison.Ordinal))
            {
                return;
            }

            foreach (var match in FindAll(text, options))
            {
                results.Add(new FileMatch(file, match));
            }
        }).ConfigureAwait(false);

        return [.. results.OrderBy(r => r.FilePath, StringComparer.Ordinal).ThenBy(r => r.Match.Offset).Take(maxResults)];
    }

    private static List<int> ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    private static int FindLine(List<int> lineStarts, int offset)
    {
        var index = lineStarts.BinarySearch(offset);
        return index >= 0 ? index : ~index - 1;
    }
}
