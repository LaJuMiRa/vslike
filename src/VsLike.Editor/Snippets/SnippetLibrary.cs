namespace VsLike.Editor.Snippets;

/// <summary>Eingebaute C#-Snippets.</summary>
public static class SnippetLibrary
{
    public static readonly IReadOnlyList<SnippetDefinition> CSharp =
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

    public static SnippetDefinition? Find(string shortcut) => CSharp.FirstOrDefault(s => s.Shortcut == shortcut);
}
