import Foundation

/// Reads diagnostics in the canonical MSBuild format from build output, e.g.
/// `/src/Program.cs(12,5): error CS1002: ; expected [/src/App.csproj]` or `App.csproj : warning NU1603: …`.
/// The keywords `error`/`warning` stay English in localized output.
public enum BuildOutputParser {
    // swiftlint:disable:next line_length
    nonisolated(unsafe) static let pattern = #/^\s*(?<origin>[^\s(][^(]*?|)(\((?<line>\d+)(,(?<column>\d+))?(,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning|info|message)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(\s+\[(?<project>[^\]]+)\])?\s*$/#.ignoresCase()

    public static func parse(line: String) -> Diagnostic? {
        guard let match = try? pattern.wholeMatch(in: line) else {
            return nil
        }
        let origin = match.output.origin.trimmingCharacters(in: .whitespaces)
        let severity: DiagnosticSeverity = switch match.output.severity.lowercased() {
        case "error": .error
        case "warning": .warning
        default: .info
        }
        let project = match.output.project.map(String.init)
        var file: String?
        if !origin.isEmpty && origin != "MSBUILD" {
            if origin.hasPrefix("/") {
                file = origin
            } else if let project {
                file = URL(fileURLWithPath: project).deletingLastPathComponent().appendingPathComponent(origin).standardizedFileURL.path
            }
        }
        return Diagnostic(
            code: String(match.output.code),
            message: match.output.message.trimmingCharacters(in: .whitespaces),
            severity: severity,
            filePath: file,
            startLine: match.output.line.flatMap { Int($0) } ?? 1,
            startColumn: match.output.column.flatMap { Int($0) } ?? 1,
            category: "Build",
            projectName: project.map { URL(fileURLWithPath: $0).deletingPathExtension().lastPathComponent }
        )
    }

    /// All distinct diagnostics of a build (MSBuild repeats them in its summary).
    public static func parse(_ lines: [OutputLine]) -> [Diagnostic] {
        var seen = Set<String>()
        return lines.compactMap { parse(line: $0.text) }.filter { seen.insert($0.id).inserted }
    }
}
