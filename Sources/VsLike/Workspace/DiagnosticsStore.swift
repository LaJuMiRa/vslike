import Foundation
import Observation
import VsLikeKit

/// All problems of the solution: live diagnostics from the engine (per file, while typing) plus diagnostics of the
/// last build. A file with live diagnostics drops the build's compiler (CS) entries, which would be stale.
@MainActor
@Observable
final class DiagnosticsStore {
    enum Filter: String, CaseIterable, Identifiable {
        case all, errors, warnings, styleCop
        var id: String { rawValue }
    }

    private(set) var live: [String: [Diagnostic]] = [:]
    private(set) var build: [Diagnostic] = []
    var filter: Filter = .all
    var search = ""

    /// Problems in display order: errors first, then by file and line. Hidden diagnostics are never shown.
    var all: [Diagnostic] {
        let liveFiles = Set(live.keys)
        let fromBuild = build.filter { !($0.code.hasPrefix("CS") && $0.filePath.map(liveFiles.contains) == true) }
        var seen = Set<String>()
        return (live.values.joined() + fromBuild)
            .filter { $0.severity != .hidden && seen.insert(key($0)).inserted }
            .sorted { lhs, rhs in
                if lhs.severity != rhs.severity { return lhs.severity > rhs.severity }
                if lhs.filePath != rhs.filePath { return (lhs.filePath ?? "") < (rhs.filePath ?? "") }
                return (lhs.startLine, lhs.startColumn) < (rhs.startLine, rhs.startColumn)
            }
    }

    var visible: [Diagnostic] {
        let needle = search.trimmingCharacters(in: .whitespaces).lowercased()
        return all.filter { diagnostic in
            let matchesFilter: Bool = switch filter {
            case .all: true
            case .errors: diagnostic.severity == .error
            case .warnings: diagnostic.severity == .warning && !diagnostic.isStyleCop
            case .styleCop: diagnostic.isStyleCop
            }
            guard matchesFilter else { return false }
            return needle.isEmpty || diagnostic.message.lowercased().contains(needle) || diagnostic.code.lowercased().contains(needle)
                || (diagnostic.filePath?.lowercased().contains(needle) ?? false)
        }
    }

    var errorCount: Int { all.filter { $0.severity == .error }.count }

    var warningCount: Int { all.filter { $0.severity == .warning }.count }

    var styleCopCount: Int { all.filter(\.isStyleCop).count }

    func diagnostics(for path: String) -> [Diagnostic] {
        if let list = live[path] {
            return list.filter { $0.severity != .hidden }
        }
        return build.filter { $0.filePath == path }
    }

    func setLive(_ diagnostics: [Diagnostic], for path: String?) {
        if let path {
            live[path] = diagnostics
        } else {
            live = Dictionary(grouping: diagnostics.filter { $0.filePath != nil }) { $0.filePath! }
        }
    }

    func setBuild(_ diagnostics: [Diagnostic]) {
        build = diagnostics
    }

    func clear() {
        live = [:]
        build = []
    }

    private func key(_ diagnostic: Diagnostic) -> String {
        "\(diagnostic.filePath ?? ""):\(diagnostic.startLine):\(diagnostic.startColumn):\(diagnostic.code)"
    }
}
