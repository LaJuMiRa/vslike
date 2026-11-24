namespace VsLike.Kit;

public enum TokenKind
{
    Keyword,
    ControlKeyword,
    TypeKeyword,
    Identifier,
    Number,
    String,
    Character,
    Comment,
    DocComment,
    Preprocessor,
    Operator,
    Punctuation,
}

/// <param name="Start">UTF-16 offset (the units of .NET strings and AvaloniaEdit).</param>
public readonly record struct Token(TokenKind Kind, int Start, int Length);

/// <summary>
/// A fast lexical tokenizer for C# used for immediate highlighting while typing. Roslyn's semantic classification
/// (types, methods, locals …) is layered on top when it arrives. Works on UTF-16 code units.
/// </summary>
public static class CSharpLexer
{
    // Contextual keywords that are common identifiers (value, args, from, select …) are left to Roslyn's semantic
    // classification, which colours them only where they act as keywords.
    internal static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "checked", "class", "const", "default", "delegate", "enum", "event", "explicit", "extern",
        "false", "fixed", "implicit", "in", "interface", "internal", "is", "lock", "namespace", "new", "null", "operator",
        "out", "override", "params", "private", "protected", "public", "readonly", "ref", "sealed", "sizeof", "stackalloc",
        "static", "struct", "this", "true", "typeof", "unchecked", "unsafe", "using", "virtual", "volatile", "record",
        "get", "set", "init", "var", "async", "await", "partial", "where", "required", "nameof", "global", "with", "when",
        "and", "or", "not", "dynamic",
    ];

    internal static readonly HashSet<string> ControlKeywords =
    [
        "if", "else", "switch", "case", "do", "for", "foreach", "while", "break", "continue", "return", "goto", "throw",
        "try", "catch", "finally", "yield",
    ];

    internal static readonly HashSet<string> TypeKeywords =
    [
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint", "long", "ulong", "short", "ushort",
        "object", "string", "void", "nint", "nuint",
    ];

    public static IReadOnlyList<Token> Tokenize(string s)
    {
        var tokens = new List<Token>(s.Length / 4);
        var i = 0;
        var n = s.Length;
        var lineStart = true;

        char At(int index) => index < n ? s[index] : '\0';
        void Add(TokenKind kind, int start, int end)
        {
            if (end > start)
            {
                tokens.Add(new Token(kind, start, end - start));
            }
        }

        while (i < n)
        {
            var c = s[i];
            if (c == '\n')
            {
                lineStart = true;
                i++;
                continue;
            }

            if (c is ' ' or '\t' or '\r')
            {
                i++;
                continue;
            }

            var start = i;
            var wasLineStart = lineStart;
            lineStart = false;

            // Preprocessor directive: # as first non-blank character of a line.
            if (c == '#' && wasLineStart)
            {
                while (i < n && s[i] != '\n')
                {
                    i++;
                }

                Add(TokenKind.Preprocessor, start, i);
                continue;
            }

            if (c == '/' && At(i + 1) == '/')
            {
                var isDoc = At(i + 2) == '/' && At(i + 3) != '/';
                while (i < n && s[i] != '\n')
                {
                    i++;
                }

                Add(isDoc ? TokenKind.DocComment : TokenKind.Comment, start, i);
                continue;
            }

            if (c == '/' && At(i + 1) == '*')
            {
                i += 2;
                while (i < n && !(s[i] == '*' && At(i + 1) == '/'))
                {
                    i++;
                }

                i = Math.Min(n, i + 2);
                Add(TokenKind.Comment, start, i);
                continue;
            }

            // Strings: prefixes $ @ (any order), raw """…""".
            if (c == '"' || ((c == '$' || c == '@') && (At(i + 1) == '"' || ((At(i + 1) == '$' || At(i + 1) == '@') && At(i + 2) == '"'))))
            {
                i = StringEnd(s, i);
                Add(TokenKind.String, start, Math.Min(i, n));
                continue;
            }

            if (c == '\'')
            {
                i++;
                while (i < n && s[i] != '\'' && s[i] != '\n')
                {
                    i += s[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(n, i + 1);
                Add(TokenKind.Character, start, i);
                continue;
            }

            if (IsDigit(c) || (c == '.' && IsDigit(At(i + 1))))
            {
                i++;
                while (i < n && (IsIdentifierPart(s[i]) || (s[i] == '.' && IsDigit(At(i + 1)))))
                {
                    i++;
                }

                Add(TokenKind.Number, start, i);
                continue;
            }

            // Identifiers and keywords (@ escapes a keyword).
            if (IsIdentifierStart(c) || (c == '@' && IsIdentifierStart(At(i + 1))))
            {
                var escaped = c == '@';
                i++;
                while (i < n && IsIdentifierPart(s[i]))
                {
                    i++;
                }

                Add(escaped ? TokenKind.Identifier : WordKind(s[start..i]), start, i);
                continue;
            }

            Add("{}()[];,.".Contains(c, StringComparison.Ordinal) ? TokenKind.Punctuation : TokenKind.Operator, start, i + 1);
            i++;
        }

        return tokens;
    }

    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static bool IsIdentifierStart(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' || c > 127;

    public static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);

    private static TokenKind WordKind(string word)
    {
        if (ControlKeywords.Contains(word))
        {
            return TokenKind.ControlKeyword;
        }

        if (TypeKeywords.Contains(word))
        {
            return TokenKind.TypeKeyword;
        }

        return Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier;
    }

    /// <summary>Returns the offset after the string literal that starts at <paramref name="i"/>.</summary>
    private static int StringEnd(string s, int i)
    {
        var n = s.Length;
        char At(int index) => index < n ? s[index] : '\0';
        var verbatim = false;
        var dollars = 0;
        while (s[i] == '$' || s[i] == '@')
        {
            if (s[i] == '@')
            {
                verbatim = true;
            }
            else
            {
                dollars++;
            }

            i++;
        }

        var quotes = 0;
        while (At(i + quotes) == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            i += quotes;
            while (i < n)
            {
                if (s[i] == '"')
                {
                    var run = 0;
                    while (At(i + run) == '"')
                    {
                        run++;
                    }

                    i += run;
                    if (run >= quotes)
                    {
                        break;
                    }
                }
                else
                {
                    i++;
                }
            }

            return i;
        }

        i++;
        var braceDepth = 0;
        while (i < n)
        {
            var ch = s[i];
            if (verbatim)
            {
                if (ch == '"')
                {
                    if (At(i + 1) == '"')
                    {
                        i += 2;
                        continue;
                    }

                    i++;
                    break;
                }
            }
            else
            {
                if (ch == '\\')
                {
                    i += 2;
                    continue;
                }

                if (ch == '\n')
                {
                    break;
                }

                if (ch == '"' && braceDepth == 0)
                {
                    i++;
                    break;
                }
            }

            if (dollars > 0)
            {
                if (ch == '{' && At(i + 1) != '{')
                {
                    braceDepth++;
                }
                else if (ch == '}' && braceDepth > 0)
                {
                    braceDepth--;
                }
            }

            i++;
        }

        return i;
    }
}
