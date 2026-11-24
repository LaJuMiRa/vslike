using System.Text.RegularExpressions;

namespace VsLike.Kit;

/// <summary>
/// Turns a file-scoped namespace (<c>namespace Demo;</c>) into a block-scoped one (<c>namespace Demo { … }</c>) with the body
/// indented one level. Used for the files that <c>dotnet new</c> creates; text inside multi-line string literals would be
/// indented too, which the templates do not contain.
/// </summary>
public static partial class NamespaceStyle
{
    public static string BlockScoped(string source, string indent = "    ")
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Split(newline);
        var index = Array.FindIndex(lines, IsFileScopedNamespace);
        if (index < 0)
        {
            return source;
        }

        var body = lines[(index + 1)..].ToList();
        while (body.Count > 0 && body[0].Trim(' ', '\t').Length == 0)
        {
            body.RemoveAt(0);
        }

        while (body.Count > 0 && body[^1].Trim(' ', '\t').Length == 0)
        {
            body.RemoveAt(body.Count - 1);
        }

        var declaration = lines[index].Replace(";", string.Empty, StringComparison.Ordinal).Trim(' ', '\t');
        var indented = body.Select(l => l.Trim(' ', '\t').Length == 0 ? string.Empty : indent + l);
        return string.Join(newline, lines[..index].Concat([declaration, "{"]).Concat(indented).Concat(["}", string.Empty]));
    }

    private static bool IsFileScopedNamespace(string line) => FileScoped().IsMatch(line.Trim(' ', '\t', '﻿'));

    [GeneratedRegex(@"^namespace\s+[A-Za-z_@][\w.@]*\s*;$")]
    private static partial Regex FileScoped();
}
