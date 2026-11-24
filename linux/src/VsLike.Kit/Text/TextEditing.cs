using System.Globalization;
using System.Text;

namespace VsLike.Kit;

/// <summary>A UTF-16 range (location and length), the counterpart of <c>NSRange</c>.</summary>
public readonly record struct TextRange(int Location, int Length)
{
    public int End => Location + Length;
}

/// <summary>
/// Text operations of the editor that do not need a UI: applying edits, indentation, comment toggling and bracket
/// matching. All offsets are UTF-16.
/// </summary>
public static class TextEditing
{
    public const string IndentUnit = "    ";

    /// <summary>Applies non-overlapping edits (any order) to a text.</summary>
    public static string Apply(IEnumerable<TextEdit> edits, string text)
    {
        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            var start = Math.Min(edit.Start, result.Length);
            result.Remove(start, Math.Min(edit.Length, result.Length - start)).Insert(start, edit.NewText);
        }

        return result.ToString();
    }

    /// <summary>The smallest single edit that turns <paramref name="before"/> into <paramref name="after"/> (common prefix and suffix removed).</summary>
    public static TextEdit MinimalEdit(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return new TextEdit(prefix, before.Length - prefix - suffix, after[prefix..(after.Length - suffix)]);
    }

    /// <summary>The range of the whole lines touched by <paramref name="range"/>, including the final line break (like <c>NSString.lineRange</c>).</summary>
    public static TextRange LineRange(string text, TextRange range)
    {
        var location = Math.Clamp(range.Location, 0, text.Length);
        var start = location > 0 ? text.LastIndexOf('\n', location - 1) + 1 : 0;
        var last = range.Length > 0 ? Math.Min(text.Length, range.End) - 1 : location;
        var newline = last < text.Length ? text.IndexOf('\n', Math.Max(last, start)) : -1;
        var end = newline < 0 ? text.Length : newline + 1;
        return new TextRange(start, Math.Max(0, end - start));
    }

    public static TextRange LineRange(string text, int offset) => LineRange(text, new TextRange(offset, 0));

    /// <summary>Leading whitespace of the line that contains <paramref name="offset"/>.</summary>
    public static string Indentation(int offset, string text)
    {
        var line = LineRange(text, Math.Min(offset, text.Length));
        var end = line.Location;
        while (end < line.End && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[line.Location..end];
    }

    /// <summary>
    /// Where Return inserts its text: between the braces when the caret sits right after an empty <c>{}</c> at the end of
    /// the line (typed with its closing brace), so that <c>{}</c> opens a block like <c>{|}</c> does; otherwise at the caret.
    /// </summary>
    public static int NewlineLocation(int offset, string text)
    {
        if (offset < 2 || offset > text.Length || text[offset - 2] != '{' || text[offset - 1] != '}')
        {
            return offset;
        }

        var line = LineRange(text, offset);
        var lineEnd = line.End - (line.End > line.Location && text[line.End - 1] == '\n' ? 1 : 0);
        var rest = offset < lineEnd ? text[offset..lineEnd] : string.Empty;
        return rest.Trim(' ', '\t').Length == 0 ? offset - 1 : offset;
    }

    /// <summary>
    /// Text to insert for Return at <paramref name="offset"/>: newline plus indentation, one level deeper after <c>{</c>, and the
    /// closing brace on its own line when the caret sits between <c>{</c> and <c>}</c>. Returns the text and the caret offset in it.
    /// </summary>
    public static (string Text, int Caret) Newline(int offset, string text)
    {
        var indent = Indentation(offset, text);
        var lineStart = LineRange(text, Math.Min(offset, text.Length)).Location;
        var before = text[lineStart..offset].Trim(' ', '\t');
        var next = offset < text.Length ? text[offset] : '\0';
        if (before.EndsWith('{') || before.EndsWith('[') || before.EndsWith('('))
        {
            var inner = "\n" + indent + IndentUnit;
            if (next is '}' or ']' or ')')
            {
                return (inner + "\n" + indent, inner.Length);
            }

            return (inner, inner.Length);
        }

        var inserted = "\n" + indent;
        return (inserted, inserted.Length);
    }

    /// <summary>
    /// The edit for Return at <paramref name="offset"/> (see <see cref="Newline"/>) and the absolute caret offset after it. When
    /// <c>{</c> ends a line of code (<c>void Run() {|}</c>), the brace moves onto its own line at the line's indentation first.
    /// </summary>
    public static (TextEdit Edit, int Caret) NewlineEdit(int offset, string text)
    {
        var inner = Newline(offset, text);
        var lineStart = LineRange(text, Math.Min(offset, text.Length)).Location;
        var before = text[lineStart..offset];
        var code = before.Trim(' ', '\t');
        var head = code.EndsWith('{') ? code[..^1].Trim(' ', '\t') : string.Empty;
        if (head.Length == 0 || head.EndsWith('"') || head.Contains("//", StringComparison.Ordinal)
            || head.StartsWith('*') || head.StartsWith("/*", StringComparison.Ordinal))
        {
            return (new TextEdit(offset, 0, inner.Text), offset + inner.Caret);
        }

        // Replace from the end of the code before `{` (dropping the spaces in between) up to the caret.
        var braceLine = "\n" + Indentation(offset, text) + "{";
        var headEnd = lineStart + before.LastIndexOf(head, StringComparison.Ordinal) + head.Length;
        return (new TextEdit(headEnd, offset - headEnd, braceLine + inner.Text), headEnd + braceLine.Length + inner.Caret);
    }

    /// <summary>
    /// Adapts the edits of a resolved completion item to the current text. The engine computes them against the text
    /// at the time the list opened, but the user may have typed more since. The edit that replaces the completion span
    /// therefore has to reach up to the current caret.
    /// </summary>
    public static IReadOnlyList<TextEdit> CompletionEdits(IEnumerable<TextEdit> edits, int spanStart, int caret) =>
        edits.Select(edit =>
            edit.Start <= spanStart && edit.Start + edit.Length >= spanStart && edit.Start + edit.Length < caret
                ? edit with { Length = caret - edit.Start }
                : edit).ToList();

    /// <summary>The closing character auto-inserted after typing <paramref name="character"/>, if any.</summary>
    public static char? ClosingPair(char character) => character switch
    {
        '(' => ')',
        '[' => ']',
        '{' => '}',
        '"' => '"',
        '\'' => '\'',
        _ => null,
    };

    /// <summary>Toggles <c>//</c> on all lines touched by <paramref name="range"/>. Returns the replaced range of whole lines and the new text.</summary>
    public static (TextRange Range, string Text) ToggleLineComment(string text, TextRange range)
    {
        var lines = LineRange(text, range);
        var parts = text.Substring(lines.Location, lines.Length).Split('\n').ToList();
        var trailingNewline = parts[^1].Length == 0;
        if (trailingNewline)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        var nonBlank = parts.Where(l => l.Trim(' ', '\t').Length > 0).ToList();
        var allCommented = nonBlank.Count > 0 && nonBlank.All(l => l.Trim(' ', '\t').StartsWith("//", StringComparison.Ordinal));
        var minIndent = nonBlank.Count == 0 ? 0 : nonBlank.Min(l => l.TakeWhile(c => c is ' ' or '\t').Count());
        var changed = parts.Select(line =>
        {
            if (line.Trim(' ', '\t').Length == 0)
            {
                return line;
            }

            if (allCommented)
            {
                var slashes = line.IndexOf("//", StringComparison.Ordinal);
                if (slashes < 0)
                {
                    return line;
                }

                var end = slashes + 2;
                if (end < line.Length && line[end] == ' ')
                {
                    end++;
                }

                return line[..slashes] + line[end..];
            }

            return line[..minIndent] + "// " + line[minIndent..];
        });
        return (lines, string.Join("\n", changed) + (trailingNewline ? "\n" : string.Empty));
    }

    /// <summary>Offset of the bracket matching the one at <paramref name="offset"/> (ignores brackets in strings and comments via the tokens).</summary>
    public static int? MatchingBracket(int offset, string units, IReadOnlyList<Token> tokens)
    {
        if (offset < 0 || offset >= units.Length)
        {
            return null;
        }

        (char Target, int Direction)? pair = units[offset] switch
        {
            '(' => (')', 1),
            '[' => (']', 1),
            '{' => ('}', 1),
            ')' => ('(', -1),
            ']' => ('[', -1),
            '}' => ('{', -1),
            _ => null,
        };
        if (pair is not { } found)
        {
            return null;
        }

        var skip = tokens.Where(t => t.Kind is TokenKind.String or TokenKind.Character or TokenKind.Comment or TokenKind.DocComment).ToList();
        bool IsSkipped(int index) => skip.Any(t => index >= t.Start && index < t.Start + t.Length);
        if (IsSkipped(offset))
        {
            return null;
        }

        var open = units[offset];
        var depth = 0;
        for (var index = offset; index >= 0 && index < units.Length; index += found.Direction)
        {
            var unit = units[index];
            if ((unit == open || unit == found.Target) && !IsSkipped(index))
            {
                depth += unit == open ? 1 : -1;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Subsequence matching for the command palette and quick open: matches at word starts and consecutive matches score
/// higher. Returns null when the query is not a subsequence of the candidate.
/// </summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string candidate)
    {
        var needle = query.ToLower(CultureInfo.CurrentCulture).Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (needle.Length == 0)
        {
            return 0;
        }

        var haystack = candidate;
        var lower = candidate.ToLower(CultureInfo.CurrentCulture);
        if (lower.Length != haystack.Length)
        {
            return lower.Contains(new string(needle), StringComparison.Ordinal) ? 1 : null;
        }

        var score = 0;
        var position = 0;
        var previous = -2;
        foreach (var character in needle)
        {
            var found = lower.IndexOf(character, position);
            if (found < 0)
            {
                return null;
            }

            score++;
            if (found == previous + 1)
            {
                score += 5;
            }

            if (found == 0 || (!char.IsLetter(haystack[found - 1]) && !char.IsDigit(haystack[found - 1]))
                || (char.IsUpper(haystack[found]) && char.IsLower(haystack[found - 1])))
            {
                score += 8;
            }

            previous = found;
            position = found + 1;
        }

        return score - (haystack.Length / 10);
    }
}
