import AppKit
import VsLikeKit

/// Colours a document: the lexer runs synchronously after every edit (no delay while typing); Roslyn's semantic
/// classification for the visible range follows debounced and is kept, shifted, across edits so that colours do not
/// flicker. Colours are temporary attributes of the layout manager (no undo, no effect on the text).
@MainActor
final class SyntaxHighlighter {
    private weak var textView: NSTextView?
    private let document: CodeDocument
    private unowned let workspace: Workspace
    private var semantic: [ClassifiedSpan] = []
    private var semanticTask: Task<Void, Never>?
    private(set) var tokens: [Token] = []
    private(set) var units: [UInt16] = []

    init(textView: NSTextView, document: CodeDocument, workspace: Workspace) {
        self.textView = textView
        self.document = document
        self.workspace = workspace
    }

    private var layoutManager: NSLayoutManager? { textView?.layoutManager }

    /// Re-colours after an edit at `editedRange` that changed the length by `delta`.
    func textChanged(editedRange: NSRange, delta: Int) {
        if editedRange.location != NSNotFound {
            shiftSemantic(editedRange: editedRange, delta: delta)
        }
        recolor()
        scheduleSemantic()
    }

    func recolor() {
        guard let layoutManager else { return }
        let string = document.storage.string
        units = Array(string.utf16)
        let full = NSRange(location: 0, length: units.count)
        layoutManager.removeTemporaryAttribute(.foregroundColor, forCharacterRange: full)
        layoutManager.removeTemporaryAttribute(.obliqueness, forCharacterRange: full)
        guard document.isCSharp else {
            if ["xml", "csproj", "props", "targets", "slnx", "config", "resx", "xaml", "axaml", "json"].contains(document.url.pathExtension.lowercased()) {
                colorMarkup(layoutManager)
            }
            return
        }
        guard units.count < 3_000_000 else { return }
        tokens = CSharpLexer.tokenize(units)
        for token in tokens where token.kind != .identifier {
            layoutManager.addTemporaryAttribute(.foregroundColor, value: SyntaxColors.color(for: token.kind), forCharacterRange: NSRange(location: token.start, length: token.length))
        }
        applySemantic()
    }

    /// XML and JSON: tags, attribute names and strings.
    private func colorMarkup(_ layoutManager: NSLayoutManager) {
        let string = document.storage.string
        let patterns: [(String, NSColor)] = [
            (#"</?[A-Za-z_][\w.:-]*|/?>"#, SyntaxColors.type),
            (#"\b[A-Za-z_][\w.:-]*(?==)"#, SyntaxColors.member),
            (#""[^"\n]*""#, SyntaxColors.string),
            (#"<!--[\s\S]*?-->"#, SyntaxColors.comment),
            (#"\b(true|false|null)\b|-?\b\d+(\.\d+)?\b"#, SyntaxColors.number),
        ]
        for (pattern, color) in patterns {
            guard let regex = try? NSRegularExpression(pattern: pattern) else { continue }
            for match in regex.matches(in: string, range: NSRange(location: 0, length: (string as NSString).length)) {
                layoutManager.addTemporaryAttribute(.foregroundColor, value: color, forCharacterRange: match.range)
            }
        }
    }

    func scheduleSemantic() {
        semanticTask?.cancel()
        guard document.isCSharp else { return }
        semanticTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(220))
            guard !Task.isCancelled else { return }
            await self?.loadSemantic()
        }
    }

    private func loadSemantic() async {
        guard let engine = workspace.engine, workspace.isEngineReady, let textView, let range = visibleRange(of: textView) else {
            return
        }
        workspace.syncNow(document)
        let version = document.version
        guard let spans = try? await engine.classify(document.path, start: range.location, length: range.length), version == document.version else {
            return
        }
        // Keep spans outside the visible range from earlier requests.
        semantic = semantic.filter { $0.start + $0.length <= range.location || $0.start >= NSMaxRange(range) } + spans
        applySemantic()
    }

    private func applySemantic() {
        guard let layoutManager else { return }
        let length = units.count
        for span in semantic where span.start >= 0 && span.start + span.length <= length {
            if let color = SyntaxColors.color(forClassification: span.classification) {
                layoutManager.addTemporaryAttribute(.foregroundColor, value: color, forCharacterRange: NSRange(location: span.start, length: span.length))
            }
        }
    }

    /// Moves cached semantic spans after an edit and drops the ones it touched.
    private func shiftSemantic(editedRange: NSRange, delta: Int) {
        let oldEnd = NSMaxRange(editedRange) - delta
        semantic = semantic.compactMap { span in
            if span.start + span.length <= editedRange.location {
                return span
            }
            if span.start >= oldEnd {
                return ClassifiedSpan(start: span.start + delta, length: span.length, classification: span.classification)
            }
            return nil
        }
    }

    private func visibleRange(of textView: NSTextView) -> NSRange? {
        guard let layoutManager = textView.layoutManager, let container = textView.textContainer else { return nil }
        var rect = textView.visibleRect
        rect = rect.insetBy(dx: 0, dy: -rect.height)
        let glyphs = layoutManager.glyphRange(forBoundingRect: rect, in: container)
        let characters = layoutManager.characterRange(forGlyphRange: glyphs, actualGlyphRange: nil)
        return characters.length > 0 ? characters : nil
    }
}
