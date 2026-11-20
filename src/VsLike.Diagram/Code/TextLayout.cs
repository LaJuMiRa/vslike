namespace VsLike.Diagram.Code;

/// <summary>Zeilenende und Einrückung einer Datei, damit eingefügter Code wie der vorhandene aussieht.</summary>
internal sealed record TextLayout(string NewLine, string IndentUnit)
{
    public static TextLayout Detect(string source)
    {
        var newLine = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = "    ";
        foreach (var line in source.Split('\n'))
        {
            if (line.StartsWith('\t'))
            {
                indent = "\t";
                break;
            }

            var spaces = line.Length - line.TrimStart(' ').Length;
            if (spaces is > 0 and < 8 && line.Trim().Length > 0)
            {
                indent = new string(' ', spaces);
                break;
            }
        }

        return new TextLayout(newLine, indent);
    }

    public static string IndentOfLine(string source, int position)
    {
        var lineStart = LineStart(source, position);
        var end = lineStart;
        while (end < source.Length && source[end] is ' ' or '\t')
        {
            end++;
        }

        return source[lineStart..end];
    }

    public static int LineStart(string source, int position)
    {
        var index = position;
        while (index > 0 && source[index - 1] != '\n')
        {
            index--;
        }

        return index;
    }

    /// <summary>Position just after the end of the line containing <paramref name="position"/> (including its newline).</summary>
    public static int NextLineStart(string source, int position)
    {
        var index = source.IndexOf('\n', position);
        return index < 0 ? source.Length : index + 1;
    }

    public string Indent(string code, string indent)
        => string.Join(NewLine, code.Split('\n').Select(l => l.Length == 0 ? l : indent + l));
}
