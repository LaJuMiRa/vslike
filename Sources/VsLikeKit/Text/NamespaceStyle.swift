import Foundation

/// Turns a file-scoped namespace (`namespace Demo;`) into a block-scoped one (`namespace Demo { … }`) with the body
/// indented one level. Used for the files that `dotnet new` creates; text inside multi-line string literals would be
/// indented too, which the templates do not contain.
public enum NamespaceStyle {
    public static func blockScoped(_ source: String, indent: String = "    ") -> String {
        let newline = source.contains("\r\n") ? "\r\n" : "\n"
        let lines = source.components(separatedBy: newline)
        guard let index = lines.firstIndex(where: isFileScopedNamespace) else {
            return source
        }
        var body = Array(lines[(index + 1)...])
        while body.first?.trimmingCharacters(in: .whitespaces).isEmpty == true { body.removeFirst() }
        while body.last?.trimmingCharacters(in: .whitespaces).isEmpty == true { body.removeLast() }
        let declaration = lines[index].replacingOccurrences(of: ";", with: "").trimmingCharacters(in: .whitespaces)
        let indented = body.map { $0.trimmingCharacters(in: .whitespaces).isEmpty ? "" : indent + $0 }
        return (Array(lines[..<index]) + [declaration, "{"] + indented + ["}", ""]).joined(separator: newline)
    }

    private static func isFileScopedNamespace(_ line: String) -> Bool {
        let trimmed = line.trimmingCharacters(in: CharacterSet.whitespaces.union(CharacterSet(charactersIn: "\u{FEFF}")))
        return trimmed.range(of: #"^namespace\s+[A-Za-z_@][\w.@]*\s*;$"#, options: .regularExpression) != nil
    }
}
