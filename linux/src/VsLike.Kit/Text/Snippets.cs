using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VsLike.Kit;

/// <param name="Body">Body with <c>${1:default}</c>, <c>$1</c>, <c>$0</c> (final caret), <c>$TYPE</c> (enclosing type) and <c>\t</c> for one indent.</param>
public sealed record Snippet(string Shortcut, string Title, string Body)
{
    public static readonly IReadOnlyList<Snippet> CSharp =
    [
        new("prop", "Auto property", "public ${1:int} ${2:MyProperty} { get; set; }$0"),
        new("propi", "Init-only property", "public ${1:int} ${2:MyProperty} { get; init; }$0"),
        new("propr", "Required property", "public required ${1:string} ${2:MyProperty} { get; init; }$0"),
        new("propfull", "Property with backing field", "private ${1:int} ${2:myVar};\n\npublic ${1:int} ${3:MyProperty}\n{\n\tget => ${2:myVar};\n\tset => ${2:myVar} = value;\n}$0"),
        new("ctor", "Constructor", "public $TYPE(${1})\n{\n\t$0\n}"),
        new("for", "for loop", "for (var ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n\t$0\n}"),
        new("forr", "Reverse for loop", "for (var ${1:i} = ${2:length} - 1; ${1:i} >= 0; ${1:i}--)\n{\n\t$0\n}"),
        new("foreach", "foreach loop", "foreach (var ${1:item} in ${2:collection})\n{\n\t$0\n}"),
        new("if", "if statement", "if (${1:true})\n{\n\t$0\n}"),
        new("else", "else block", "else\n{\n\t$0\n}"),
        new("while", "while loop", "while (${1:true})\n{\n\t$0\n}"),
        new("do", "do-while loop", "do\n{\n\t$0\n}\nwhile (${1:true});"),
        new("switch", "switch statement", "switch (${1:value})\n{\n\tcase ${2:0}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}"),
        new("try", "try/catch", "try\n{\n\t$0\n}\ncatch (${1:Exception} ${2:ex})\n{\n\tthrow;\n}"),
        new("tryf", "try/finally", "try\n{\n\t$0\n}\nfinally\n{\n}"),
        new("using", "using statement", "using (${1:resource})\n{\n\t$0\n}"),
        new("lock", "lock statement", "lock (${1:this})\n{\n\t$0\n}"),
        new("cw", "Console.WriteLine", "Console.WriteLine(${1});$0"),
        new("class", "Class", "public class ${1:MyClass}\n{\n\t$0\n}"),
        new("interface", "Interface", "public interface ${1:IMyInterface}\n{\n\t$0\n}"),
        new("record", "Record", "public record ${1:MyRecord}(${2});$0"),
        new("struct", "Struct", "public struct ${1:MyStruct}\n{\n\t$0\n}"),
        new("enum", "Enum", "public enum ${1:MyEnum}\n{\n\t$0\n}"),
        new("svm", "static void Main", "static void Main(string[] args)\n{\n\t$0\n}"),
        new("test", "xUnit test", "[Fact]\npublic void ${1:Method_Condition_Result}()\n{\n\t$0\n}"),
    ];

    public string Id => Shortcut;

    public static Snippet? Find(string shortcut) => CSharp.FirstOrDefault(s => s.Shortcut == shortcut);
}

/// <summary>An expanded snippet: the text plus tab stops (UTF-16 ranges relative to the inserted text).</summary>
/// <param name="Placeholders">Ordered by index, then position. Placeholders with the same index are linked.</param>
public sealed record ExpandedSnippet(string Text, IReadOnlyList<ExpandedSnippet.Placeholder> Placeholders, int FinalCaret)
{
    /// <summary>Gets the distinct tab stop indices in order.</summary>
    public IReadOnlyList<int> Stops => [.. Placeholders.Select(p => p.Index).Distinct().Order()];

    public bool Equals(ExpandedSnippet? other) =>
        other is not null && Text == other.Text && FinalCaret == other.FinalCaret && Placeholders.SequenceEqual(other.Placeholders);

    public override int GetHashCode() => HashCode.Combine(Text, FinalCaret, Placeholders.Count);

    public readonly record struct Placeholder(int Index, int Start, int Length);
}

public static partial class SnippetExpander
{
    public static ExpandedSnippet Expand(string body, string indentation, string? typeName, string indentUnit = "    ")
    {
        var source = body.Replace("$TYPE", typeName ?? "MyType", StringComparison.Ordinal).Replace("\t", indentUnit, StringComparison.Ordinal);
        var output = new StringBuilder();
        var placeholders = new List<ExpandedSnippet.Placeholder>();
        int? finalCaret = null;
        var lines = source.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (lineIndex > 0)
            {
                output.Append('\n').Append(indentation);
            }

            var position = 0;
            foreach (Match match in Token().Matches(line))
            {
                output.Append(line, position, match.Index - position);
                var number = match.Groups["n1"].Success ? match.Groups["n1"].Value : match.Groups["n2"].Value;
                var index = int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                var text = match.Groups["text"].Success ? match.Groups["text"].Value : string.Empty;
                if (index == 0)
                {
                    finalCaret = output.Length;
                }
                else
                {
                    placeholders.Add(new ExpandedSnippet.Placeholder(index, output.Length, text.Length));
                    output.Append(text);
                }

                position = match.Index + match.Length;
            }

            output.Append(line, position, line.Length - position);
        }

        var ordered = placeholders.OrderBy(p => p.Index).ThenBy(p => p.Start).ToList();
        return new ExpandedSnippet(output.ToString(), ordered, finalCaret ?? output.Length);
    }

    [GeneratedRegex(@"\$\{(?<n1>\d+)(?::(?<text>[^}]*))?\}|\$(?<n2>\d+)")]
    private static partial Regex Token();
}
