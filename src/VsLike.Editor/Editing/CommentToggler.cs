namespace VsLike.Editor.Editing;

/// <summary>Kommentiert Zeilen ein oder aus (Cmd+/).</summary>
public static class CommentToggler
{
    /// <summary>
    /// Toggles line comments: if every non-blank line is commented, the comments are removed,
    /// otherwise every non-blank line gets the token at the smallest common indentation.
    /// </summary>
    public static IReadOnlyList<string> ToggleLineComments(IReadOnlyList<string> lines, string token)
    {
        var nonBlank = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonBlank.Count == 0)
        {
            return lines;
        }

        var allCommented = nonBlank.All(l => l.TrimStart().StartsWith(token, StringComparison.Ordinal));
        if (allCommented)
        {
            return [.. lines.Select(l => Uncomment(l, token))];
        }

        var indent = nonBlank.Min(l => l.Length - l.TrimStart().Length);
        return [.. lines.Select(l => string.IsNullOrWhiteSpace(l) ? l : l[..indent] + token + " " + l[indent..])];
    }

    /// <summary>Wraps or unwraps a block comment around <paramref name="text"/>.</summary>
    public static string ToggleBlockComment(string text, string start, string end)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith(start, StringComparison.Ordinal) && trimmed.EndsWith(end, StringComparison.Ordinal))
        {
            var leading = text[..(text.Length - text.TrimStart().Length)];
            var trailing = text[text.TrimEnd().Length..];
            var inner = trimmed[start.Length..^end.Length];
            return leading + inner.Trim() + trailing;
        }

        return $"{start} {text} {end}";
    }

    private static string Uncomment(string line, string token)
    {
        var index = line.IndexOf(token, StringComparison.Ordinal);
        if (index < 0 || !string.IsNullOrWhiteSpace(line[..index]))
        {
            return line;
        }

        var after = index + token.Length;
        if (after < line.Length && line[after] == ' ')
        {
            after++;
        }

        return line[..index] + line[after..];
    }
}
