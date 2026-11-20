import Foundation

public struct Snippet: Sendable, Hashable, Identifiable {
    public let shortcut: String
    public let title: String
    /// Body with `${1:default}`, `$1`, `$0` (final caret), `$TYPE` (enclosing type) and `\t` for one indent.
    public let body: String

    public var id: String { shortcut }

    public static let csharp: [Snippet] = [
        Snippet(shortcut: "prop", title: "Auto property", body: "public ${1:int} ${2:MyProperty} { get; set; }$0"),
        Snippet(shortcut: "propi", title: "Init-only property", body: "public ${1:int} ${2:MyProperty} { get; init; }$0"),
        Snippet(shortcut: "propr", title: "Required property", body: "public required ${1:string} ${2:MyProperty} { get; init; }$0"),
        Snippet(shortcut: "propfull", title: "Property with backing field", body: "private ${1:int} ${2:myVar};\n\npublic ${1:int} ${3:MyProperty}\n{\n\tget => ${2:myVar};\n\tset => ${2:myVar} = value;\n}$0"),
        Snippet(shortcut: "ctor", title: "Constructor", body: "public $TYPE(${1})\n{\n\t$0\n}"),
        Snippet(shortcut: "for", title: "for loop", body: "for (var ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n\t$0\n}"),
        Snippet(shortcut: "forr", title: "Reverse for loop", body: "for (var ${1:i} = ${2:length} - 1; ${1:i} >= 0; ${1:i}--)\n{\n\t$0\n}"),
        Snippet(shortcut: "foreach", title: "foreach loop", body: "foreach (var ${1:item} in ${2:collection})\n{\n\t$0\n}"),
        Snippet(shortcut: "if", title: "if statement", body: "if (${1:true})\n{\n\t$0\n}"),
        Snippet(shortcut: "else", title: "else block", body: "else\n{\n\t$0\n}"),
        Snippet(shortcut: "while", title: "while loop", body: "while (${1:true})\n{\n\t$0\n}"),
        Snippet(shortcut: "do", title: "do-while loop", body: "do\n{\n\t$0\n}\nwhile (${1:true});"),
        Snippet(shortcut: "switch", title: "switch statement", body: "switch (${1:value})\n{\n\tcase ${2:0}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}"),
        Snippet(shortcut: "try", title: "try/catch", body: "try\n{\n\t$0\n}\ncatch (${1:Exception} ${2:ex})\n{\n\tthrow;\n}"),
        Snippet(shortcut: "tryf", title: "try/finally", body: "try\n{\n\t$0\n}\nfinally\n{\n}"),
        Snippet(shortcut: "using", title: "using statement", body: "using (${1:resource})\n{\n\t$0\n}"),
        Snippet(shortcut: "lock", title: "lock statement", body: "lock (${1:this})\n{\n\t$0\n}"),
        Snippet(shortcut: "cw", title: "Console.WriteLine", body: "Console.WriteLine(${1});$0"),
        Snippet(shortcut: "class", title: "Class", body: "public class ${1:MyClass}\n{\n\t$0\n}"),
        Snippet(shortcut: "interface", title: "Interface", body: "public interface ${1:IMyInterface}\n{\n\t$0\n}"),
        Snippet(shortcut: "record", title: "Record", body: "public record ${1:MyRecord}(${2});$0"),
        Snippet(shortcut: "struct", title: "Struct", body: "public struct ${1:MyStruct}\n{\n\t$0\n}"),
        Snippet(shortcut: "enum", title: "Enum", body: "public enum ${1:MyEnum}\n{\n\t$0\n}"),
        Snippet(shortcut: "svm", title: "static void Main", body: "static void Main(string[] args)\n{\n\t$0\n}"),
        Snippet(shortcut: "test", title: "xUnit test", body: "[Fact]\npublic void ${1:Method_Condition_Result}()\n{\n\t$0\n}"),
    ]

    public static func find(_ shortcut: String) -> Snippet? { csharp.first { $0.shortcut == shortcut } }
}

/// An expanded snippet: the text plus tab stops (UTF-16 ranges relative to the inserted text).
public struct ExpandedSnippet: Sendable, Hashable {
    public struct Placeholder: Sendable, Hashable {
        public let index: Int
        public let start: Int
        public let length: Int
    }

    public let text: String
    /// Ordered by index, then position. Placeholders with the same index are linked.
    public let placeholders: [Placeholder]
    public let finalCaret: Int

    /// Distinct tab stop indices in order.
    public var stops: [Int] { Array(Set(placeholders.map(\.index))).sorted() }
}

public enum SnippetExpander {
    public static func expand(_ body: String, indentation: String, typeName: String?, indentUnit: String = "    ") -> ExpandedSnippet {
        let source = body.replacingOccurrences(of: "$TYPE", with: typeName ?? "MyType").replacingOccurrences(of: "\t", with: indentUnit)
        var output = ""
        var length = 0
        var placeholders: [ExpandedSnippet.Placeholder] = []
        var finalCaret: Int?
        let token = #/\$\{(?<n1>\d+)(?::(?<text>[^}]*))?\}|\$(?<n2>\d+)/#
        for (lineIndex, line) in source.components(separatedBy: "\n").enumerated() {
            if lineIndex > 0 {
                output += "\n" + indentation
                length += 1 + indentation.utf16.count
            }
            var position = line.startIndex
            for match in line.matches(of: token) {
                let before = line[position..<match.range.lowerBound]
                output += before
                length += before.utf16.count
                let index = Int(match.output.n1 ?? match.output.n2 ?? "0") ?? 0
                let text = String(match.output.text ?? "")
                if index == 0 {
                    finalCaret = length
                } else {
                    placeholders.append(.init(index: index, start: length, length: text.utf16.count))
                    output += text
                    length += text.utf16.count
                }
                position = match.range.upperBound
            }
            let rest = line[position...]
            output += rest
            length += rest.utf16.count
        }
        let ordered = placeholders.sorted { ($0.index, $0.start) < ($1.index, $1.start) }
        return ExpandedSnippet(text: output, placeholders: ordered, finalCaret: finalCaret ?? length)
    }
}
