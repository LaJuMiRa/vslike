import AppKit
import Observation
import VsLikeKit

/// An open text file. The text lives in an `NSTextStorage` shared with the editor view; undo is per document.
@MainActor
@Observable
final class CodeDocument: Identifiable {
    let id = UUID()
    private(set) var url: URL
    @ObservationIgnored let storage: NSTextStorage
    @ObservationIgnored let undoManager = UndoManager()

    /// Bumped on every text change (the editor and the engine sync react to it).
    private(set) var version = 0
    private(set) var isDirty = false
    var breakpoints: [Int: SourceBreakpoint] = [:]
    /// 1-based line where the debugger is stopped in this file.
    var executionLine: Int?
    /// Pending caret target applied when the editor shows the document.
    var pendingSelection: NSRange?

    @ObservationIgnored private var savedVersion = 0
    @ObservationIgnored var onChange: ((CodeDocument) -> Void)?

    init(url: URL, text: String) {
        self.url = url
        storage = NSTextStorage(string: text)
    }

    var path: String { url.path }

    var name: String { url.lastPathComponent }

    var text: String { storage.string }

    var isCSharp: Bool { url.pathExtension.lowercased() == "cs" }

    var languageName: String {
        switch url.pathExtension.lowercased() {
        case "cs": return "C#"
        case "csproj", "props", "targets", "xml", "slnx", "config", "resx", "xaml", "axaml": return "XML"
        case "json": return "JSON"
        case "md": return "Markdown"
        default: return String(localized: "Plain Text")
        }
    }

    static func load(_ url: URL) throws -> CodeDocument {
        let data = try Data(contentsOf: url)
        let text = String(data: data, encoding: .utf8) ?? String(decoding: data, as: UTF8.self)
        return CodeDocument(url: url, text: text.replacingOccurrences(of: "\r\n", with: "\n"))
    }

    /// Called by the editor after every edit (typing, undo, programmatic changes).
    func textDidChange() {
        version += 1
        isDirty = version != savedVersion
        onChange?(self)
    }

    func save() throws {
        try text.write(to: url, atomically: true, encoding: .utf8)
        savedVersion = version
        isDirty = false
    }

    func rename(to newURL: URL) {
        url = newURL
    }

    /// Replaces the whole text after an external change on disk (kept undoable).
    func reload(from disk: String) {
        guard disk != text else {
            return
        }
        replace(NSRange(location: 0, length: storage.length), with: disk, actionName: String(localized: "Reload"))
        savedVersion = version
        isDirty = false
    }

    /// An undoable replacement outside of typing (code fixes, rename, diagram changes, formatting).
    func replace(_ range: NSRange, with replacement: String, actionName: String? = nil) {
        let clamped = NSRange(location: min(range.location, storage.length), length: min(range.length, max(0, storage.length - range.location)))
        let previous = storage.attributedSubstring(from: clamped).string
        let undoRange = NSRange(location: clamped.location, length: (replacement as NSString).length)
        undoManager.registerUndo(withTarget: self) { document in
            MainActor.assumeIsolated {
                document.replace(undoRange, with: previous, actionName: actionName)
            }
        }
        if let actionName {
            undoManager.setActionName(actionName)
        }
        storage.beginEditing()
        storage.replaceCharacters(in: clamped, with: replacement)
        storage.endEditing()
        textDidChange()
    }

    /// Applies several edits as one undo step (offsets refer to the text before the edits).
    func apply(_ edits: [TextEdit], actionName: String) {
        undoManager.beginUndoGrouping()
        for edit in edits.sorted(by: { $0.start > $1.start }) {
            replace(NSRange(location: edit.start, length: edit.length), with: edit.newText, actionName: actionName)
        }
        undoManager.endUndoGrouping()
        undoManager.setActionName(actionName)
    }

    /// UTF-16 offset of a 1-based line and column.
    func offset(line: Int, column: Int) -> Int {
        let string = storage.string as NSString
        var current = 1
        var index = 0
        while current < line && index < string.length {
            let range = string.lineRange(for: NSRange(location: index, length: 0))
            index = NSMaxRange(range)
            current += 1
        }
        return min(string.length, index + max(0, column - 1))
    }

    /// 1-based line and column of an offset.
    func position(of offset: Int) -> (line: Int, column: Int) {
        let string = storage.string as NSString
        let clamped = min(offset, string.length)
        var line = 1
        var index = 0
        while index < clamped {
            let range = string.lineRange(for: NSRange(location: index, length: 0))
            if NSMaxRange(range) > clamped {
                break
            }
            index = NSMaxRange(range)
            line += 1
        }
        return (line, clamped - index + 1)
    }

    func toggleBreakpoint(line: Int) {
        if breakpoints[line] != nil {
            breakpoints[line] = nil
        } else {
            breakpoints[line] = SourceBreakpoint(line: line)
        }
    }
}
