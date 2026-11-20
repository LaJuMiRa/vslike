using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace VsLike.Editor.Folding;

/// <summary>
/// Faltbereiche für C# und JSON: geschweifte Klammern (ohne Klammern in Strings/Kommentaren) und #region-Blöcke.
/// </summary>
public static partial class BraceFoldingStrategy
{
    public static IReadOnlyList<NewFolding> Compute(string text)
    {
        var result = new List<NewFolding>();
        var braces = new Stack<int>();
        var lineStarts = LineStarts(text);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                i = SkipToLineEnd(text, i);
            }
            else if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                AddIfMultiline(result, lineStarts, i, stop, "/* … */");
                i = stop;
            }
            else if (c == '@' && next == '"')
            {
                i = SkipVerbatimString(text, i + 2);
            }
            else if (c == '"' || c == '\'')
            {
                i = SkipString(text, i + 1, c);
            }
            else
            {
                if (c == '{')
                {
                    braces.Push(i);
                }
                else if (c == '}' && braces.Count > 0)
                {
                    AddIfMultiline(result, lineStarts, braces.Pop(), i + 1, "{…}");
                }

                i++;
            }
        }

        AddRegions(text, result);
        result.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return result;
    }

    public static void Update(FoldingManager manager, TextDocument document)
        => manager.UpdateFoldings(Compute(document.Text), -1);

    [GeneratedRegex(@"^[ \t]*#(end)?region\b(.*)$", RegexOptions.Multiline)]
    private static partial Regex RegionRegex();

    private static void AddRegions(string text, List<NewFolding> result)
    {
        var open = new Stack<(int Offset, string Name)>();
        foreach (Match match in RegionRegex().Matches(text))
        {
            if (match.Groups[1].Success)
            {
                if (open.Count > 0)
                {
                    var (start, name) = open.Pop();
                    result.Add(new NewFolding(start, match.Index + match.Length) { Name = name });
                }
            }
            else
            {
                var name = match.Groups[2].Value.Trim();
                open.Push((match.Index + match.Value.Length - match.Value.TrimStart().Length, name.Length == 0 ? "#region" : name));
            }
        }
    }

    private static void AddIfMultiline(List<NewFolding> result, List<int> lineStarts, int start, int end, string name)
    {
        if (LineOf(lineStarts, start) != LineOf(lineStarts, end - 1))
        {
            result.Add(new NewFolding(start, end) { Name = name });
        }
    }

    private static int SkipToLineEnd(string text, int i)
    {
        var end = text.IndexOf('\n', i);
        return end < 0 ? text.Length : end;
    }

    private static int SkipString(string text, int i, char quote)
    {
        while (i < text.Length && text[i] != quote && text[i] != '\n')
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, text.Length);
    }

    private static int SkipVerbatimString(string text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    private static List<int> LineStarts(string text)
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

    private static int LineOf(List<int> starts, int offset)
    {
        var index = starts.BinarySearch(offset);
        return index >= 0 ? index : ~index - 1;
    }
}
