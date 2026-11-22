namespace VsLike.Diagram.Model;

/// <summary>
/// Liest Mitglieder in der UML-Notation der Boxen: <c>+ Total(withTax: bool): decimal</c>, <c>- _items: List&lt;Item&gt;</c>,
/// <c>Name: string = ""</c>. Nur der Name ist Pflicht: ohne Zugriffssymbol (+ - # ~) ist das Mitglied öffentlich, ohne Typ
/// <c>int</c> (Methoden: <c>void</c>, Ereignisse: <c>EventHandler</c>), ohne Klammern hat eine Methode keine Parameter.
/// </summary>
public static class MemberSpec
{
    /// <summary>Typ für Felder, Properties und Parameter, wenn keiner angegeben ist.</summary>
    public const string DefaultType = "int";

    /// <summary>Rückgabetyp für Methoden, wenn keiner angegeben ist.</summary>
    public const string DefaultReturnType = "void";

    /// <summary>Typ für Ereignisse, wenn keiner angegeben ist.</summary>
    public const string DefaultEventType = "EventHandler";

    public static bool TryParse(MemberKind kind, string text, string ownerName, out DiagramMember member)
    {
        member = new DiagramMember(kind, string.Empty);
        var rest = text.Trim();
        var access = Access.Public;
        if (rest.Length > 0 && "+-#~".Contains(rest[0], StringComparison.Ordinal))
        {
            access = rest[0] switch { '+' => Access.Public, '-' => Access.Private, '#' => Access.Protected, _ => Access.Internal };
            rest = rest[1..].Trim();
        }

        string? value = null;
        var equals = TopLevelIndexOf(rest, '=');
        if (equals > 0 && kind is MemberKind.Field or MemberKind.Property or MemberKind.EnumValue)
        {
            value = rest[(equals + 1)..].Trim();
            rest = rest[..equals].Trim();
        }

        List<DiagramParameter>? parameters = null;
        var open = rest.IndexOf('(', StringComparison.Ordinal);
        if (kind is MemberKind.Method or MemberKind.Constructor)
        {
            var close = open < 0 ? -1 : MatchingParen(rest, open);
            if (close < 0)
            {
                if (kind == MemberKind.Constructor && open < 0)
                {
                    rest = "(" + rest + ")";
                    open = 0;
                    close = rest.Length - 1;
                }
                else if (open < 0)
                {
                    var colon = TopLevelIndexOf(rest, ':');
                    open = colon < 0 ? rest.Length : colon;
                    rest = rest.Insert(open, "()");
                    close = open + 1;
                }
                else
                {
                    return false;
                }
            }

            parameters = [];
            foreach (var part in SplitTopLevel(rest[(open + 1)..close], ','))
            {
                var colon = part.IndexOf(':', StringComparison.Ordinal);
                var parameterName = (colon < 0 ? part : part[..colon]).Trim();
                var parameterType = colon < 0 ? string.Empty : part[(colon + 1)..].Trim();
                if (!IsIdentifier(parameterName))
                {
                    return false;
                }

                parameters.Add(new DiagramParameter(parameterName, parameterType.Length == 0 ? DefaultType : parameterType));
            }

            rest = rest[..open] + rest[(close + 1)..];
        }

        var typeColon = TopLevelIndexOf(rest, ':');
        var name = (typeColon < 0 ? rest : rest[..typeColon]).Trim();
        var type = typeColon < 0 ? string.Empty : rest[(typeColon + 1)..].Trim();
        if (kind == MemberKind.Constructor)
        {
            name = ownerName;
        }

        if (!IsIdentifier(name))
        {
            return false;
        }

        if (type.Length == 0)
        {
            type = kind switch
            {
                MemberKind.Field or MemberKind.Property => DefaultType,
                MemberKind.Method => DefaultReturnType,
                MemberKind.Event => DefaultEventType,
                _ => type,
            };
        }

        member = new DiagramMember(kind, name, type, access, Parameters: parameters, Value: value);
        return true;
    }

    private static bool IsIdentifier(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_' || name[0] == '@') && name.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int TopLevelIndexOf(string text, char target)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<' or '(' or '[':
                    depth++;
                    break;
                case '>' or ')' or ']':
                    depth--;
                    break;
                default:
                    if (text[i] == target && depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '<' or '(' or '[' => 1, '>' or ')' or ']' => -1, _ => 0 };
            if (text[i] == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        if (text[start..].Trim().Length > 0)
        {
            yield return text[start..];
        }
    }
}
