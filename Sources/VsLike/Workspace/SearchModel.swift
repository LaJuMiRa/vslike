import Foundation
import Observation

/// Find (and replace) in all files of the open folder. Runs off the main thread; open documents use their
/// unsaved text.
@MainActor
@Observable
final class SearchModel {
    struct Match: Identifiable, Hashable {
        let id: String
        let line: Int
        let column: Int
        let length: Int
        let preview: String
    }

    struct FileResult: Identifiable, Hashable {
        var id: String { path }
        let path: String
        let matches: [Match]
    }

    @ObservationIgnored private unowned let workspace: Workspace
    @ObservationIgnored private var task: Task<Void, Never>?

    var query = ""
    var replacement = ""
    var caseSensitive = false
    var wholeWord = false
    var useRegex = false
    private(set) var results: [FileResult] = []
    private(set) var isSearching = false
    private(set) var errorText: String?

    init(workspace: Workspace) {
        self.workspace = workspace
    }

    var matchCount: Int { results.map(\.matches.count).reduce(0, +) }

    private func expression() throws -> NSRegularExpression? {
        guard !query.isEmpty else { return nil }
        var pattern = useRegex ? query : NSRegularExpression.escapedPattern(for: query)
        if wholeWord {
            pattern = "\\b" + pattern + "\\b"
        }
        return try NSRegularExpression(pattern: pattern, options: caseSensitive ? [] : [.caseInsensitive])
    }

    func run() {
        task?.cancel()
        errorText = nil
        guard let root = workspace.rootURL else { return }
        let regex: NSRegularExpression
        do {
            guard let built = try expression() else {
                results = []
                return
            }
            regex = built
        } catch {
            errorText = String(localized: "Invalid regular expression")
            return
        }
        let openTexts = Dictionary(workspace.openDocuments.map { ($0.path, $0.text) }, uniquingKeysWith: { first, _ in first })
        let files = FileTreeBuilder.allFiles(workspace.folderTree).compactMap(\.url?.path)
        isSearching = true
        task = Task.detached(priority: .userInitiated) { [weak self] in
            var found: [FileResult] = []
            for path in files {
                if Task.isCancelled { return }
                guard let text = openTexts[path] ?? Self.readText(path) else { continue }
                let matches = Self.matches(regex, in: text, path: path)
                if !matches.isEmpty {
                    found.append(FileResult(path: path, matches: matches))
                }
                if found.map(\.matches.count).reduce(0, +) > 5000 { break }
            }
            let sorted = found.sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
            await MainActor.run {
                guard let self, !Task.isCancelled else { return }
                self.results = sorted
                self.isSearching = false
            }
        }
        _ = root
    }

    nonisolated private static func readText(_ path: String) -> String? {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: path),
              (attributes[.size] as? Int ?? 0) < 4_000_000,
              let data = FileManager.default.contents(atPath: path), !data.prefix(1024).contains(0) else {
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    nonisolated private static func matches(_ regex: NSRegularExpression, in text: String, path: String) -> [Match] {
        let string = text as NSString
        var result: [Match] = []
        var lineNumber = 1
        var lineStart = 0
        var scanned = 0
        for match in regex.matches(in: text, range: NSRange(location: 0, length: string.length)) where match.range.length > 0 {
            while scanned < match.range.location {
                if string.character(at: scanned) == 10 {
                    lineNumber += 1
                    lineStart = scanned + 1
                }
                scanned += 1
            }
            let lineRange = string.lineRange(for: NSRange(location: lineStart, length: 0))
            let preview = string.substring(with: lineRange).trimmingCharacters(in: .whitespacesAndNewlines)
            result.append(Match(id: "\(path):\(match.range.location)", line: lineNumber, column: match.range.location - lineStart + 1, length: match.range.length, preview: preview))
        }
        return result
    }

    /// Replaces all matches in all files (open documents undoably).
    func replaceAll() {
        guard let regex = try? expression() else { return }
        for file in results {
            if let document = workspace.openDocuments.first(where: { $0.path == file.path }) {
                let template = useRegex ? replacement : NSRegularExpression.escapedTemplate(for: replacement)
                let replaced = regex.stringByReplacingMatches(in: document.text, range: NSRange(location: 0, length: (document.text as NSString).length), withTemplate: template)
                document.replace(NSRange(location: 0, length: document.storage.length), with: replaced, actionName: String(localized: "Replace All"))
            } else if let text = Self.readText(file.path) {
                let template = useRegex ? replacement : NSRegularExpression.escapedTemplate(for: replacement)
                let replaced = regex.stringByReplacingMatches(in: text, range: NSRange(location: 0, length: (text as NSString).length), withTemplate: template)
                try? replaced.write(toFile: file.path, atomically: true, encoding: .utf8)
            }
        }
        run()
    }
}
