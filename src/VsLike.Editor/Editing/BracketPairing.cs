namespace VsLike.Editor.Editing;

/// <summary>Regeln für automatisches Schließen von Klammern und Anführungszeichen.</summary>
public static class BracketPairing
{
    private static readonly Dictionary<char, char> Pairs = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"',
        ['\''] = '\'',
    };

    public static char? ClosingFor(char opening) => Pairs.TryGetValue(opening, out var closing) ? closing : null;

    /// <summary>True when typing <paramref name="typed"/> should insert the closing partner as well.</summary>
    public static bool ShouldAutoClose(char typed, char? previous, char? next)
    {
        if (!Pairs.ContainsKey(typed))
        {
            return false;
        }

        var nextAllowsClose = next is null || char.IsWhiteSpace(next.Value) || next is ')' or ']' or '}' or ';' or ',';
        if (typed is '"' or '\'')
        {
            var previousAllows = previous is null || !(char.IsLetterOrDigit(previous.Value) || previous == typed || previous == '\\');
            return nextAllowsClose && previousAllows;
        }

        return nextAllowsClose;
    }

    /// <summary>True when the typed closing character should just move over the existing one.</summary>
    public static bool ShouldOvertype(char typed, char? next) => next == typed && typed is ')' or ']' or '}' or '"' or '\'';

    /// <summary>True when backspace between an empty pair should delete both characters.</summary>
    public static bool ShouldDeletePair(char? previous, char? next)
        => previous is { } p && next is { } n && ClosingFor(p) == n;
}
