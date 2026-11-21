import AppKit
import SwiftUI
import VsLikeKit

/// The code editor: an NSTextView (TextKit 1 for predictable, fast layout of long files) with C# editing behaviour,
/// IntelliSense popups, quick actions (⌘⇧.), snippets, breakpoints and diagnostics.
@MainActor
final class CodeTextView: NSTextView {
    private(set) var document: CodeDocument?
    unowned var workspace: Workspace!
    var highlighter: SyntaxHighlighter?
    let completion = CompletionPopup()
    let signaturePopup = InfoPopup()
    let hoverPopup = InfoPopup()
    var completionList: CompletionList?
    var completionStart = 0
    var completionTask: Task<Void, Never>?
    var signatureTask: Task<Void, Never>?
    var hoverTask: Task<Void, Never>?
    var detailTask: Task<Void, Never>?
    var snippet: SnippetSession?
    var bracketHighlight: NSRange?
    var lineStarts: [Int] = [0]
    private(set) var diagnosticSeverityByLine: [Int: DiagnosticSeverity] = [:]
    var diagnostics: [Diagnostic] = []
    var trackingArea: NSTrackingArea?

    static func make(document: CodeDocument, workspace: Workspace) -> (NSScrollView, CodeTextView) {
        let layoutManager = SquiggleLayoutManager()
        layoutManager.allowsNonContiguousLayout = true
        document.storage.addLayoutManager(layoutManager)
        let container = NSTextContainer(size: NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude))
        container.widthTracksTextView = false
        layoutManager.addTextContainer(container)

        let textView = CodeTextView(frame: NSRect(x: 0, y: 0, width: 800, height: 600), textContainer: container)
        textView.document = document
        textView.workspace = workspace
        textView.configure()

        let scroll = NSScrollView()
        scroll.documentView = textView
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = true
        scroll.backgroundColor = Palette.editorBackground
        scroll.verticalScroller = MarkerScroller()
        scroll.scrollerStyle = .overlay
        textView.minSize = NSSize(width: 0, height: 0)
        textView.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        textView.isVerticallyResizable = true
        textView.isHorizontallyResizable = true
        textView.autoresizingMask = [.width]

        let gutter = LineGutter(textView: textView)
        scroll.verticalRulerView = gutter
        scroll.hasVerticalRuler = true
        scroll.rulersVisible = true
        gutter.onToggleBreakpoint = { [weak textView] line in textView?.toggleBreakpoint(line: line) }
        gutter.onEditCondition = { [weak textView] line in textView?.editBreakpointCondition(line: line) }

        document.storage.delegate = textView
        textView.highlighter = SyntaxHighlighter(textView: textView, document: document, workspace: workspace)
        textView.rebuildLineStarts()
        textView.highlighter?.recolor()
        textView.highlighter?.scheduleSemantic()
        NotificationCenter.default.addObserver(textView, selector: #selector(scrolled), name: NSView.boundsDidChangeNotification, object: scroll.contentView)
        scroll.contentView.postsBoundsChangedNotifications = true
        return (scroll, textView)
    }

    func configure() {
        font = workspace.preferences.editorFont
        isRichText = false
        importsGraphics = false
        allowsUndo = true
        usesFindBar = true
        isIncrementalSearchingEnabled = true
        isAutomaticQuoteSubstitutionEnabled = false
        isAutomaticDashSubstitutionEnabled = false
        isAutomaticTextReplacementEnabled = false
        isAutomaticSpellingCorrectionEnabled = false
        isContinuousSpellCheckingEnabled = false
        isGrammarCheckingEnabled = false
        smartInsertDeleteEnabled = false
        isAutomaticLinkDetectionEnabled = false
        isAutomaticDataDetectionEnabled = false
        isAutomaticTextCompletionEnabled = false
        drawsBackground = true
        backgroundColor = Palette.editorBackground
        insertionPointColor = Palette.accent
        selectedTextAttributes = [.backgroundColor: Palette.selection]
        textColor = SyntaxColors.text
        typingAttributes = [.font: workspace.preferences.editorFont, .foregroundColor: SyntaxColors.text, .paragraphStyle: Self.paragraphStyle(for: workspace.preferences.editorFont)]
        defaultParagraphStyle = Self.paragraphStyle(for: workspace.preferences.editorFont)
        textContainerInset = NSSize(width: 6, height: 8)
        document?.storage.setAttributes(typingAttributes, range: NSRange(location: 0, length: document?.storage.length ?? 0))
        delegate = self
        completion.onCommit = { [weak self] item in self?.commitCompletion(item) }
        completion.onSelectionChange = { [weak self] item in self?.loadCompletionDetail(item) }
    }

    static func paragraphStyle(for font: NSFont) -> NSParagraphStyle {
        let style = NSMutableParagraphStyle()
        let tabWidth = ("    " as NSString).size(withAttributes: [.font: font]).width
        style.defaultTabInterval = tabWidth
        style.tabStops = []
        style.lineHeightMultiple = 1.18
        return style
    }

    func applyFont() {
        let font = workspace.preferences.editorFont
        guard font != self.font else { return }
        self.font = font
        typingAttributes[.font] = font
        typingAttributes[.paragraphStyle] = Self.paragraphStyle(for: font)
        document?.storage.addAttributes([.font: font, .paragraphStyle: Self.paragraphStyle(for: font)], range: NSRange(location: 0, length: document?.storage.length ?? 0))
        (enclosingScrollView?.verticalRulerView as? LineGutter)?.needsDisplay = true
    }

    override var undoManager: UndoManager? { document?.undoManager }

    // MARK: Lines

    var lineCount: Int { lineStarts.count }

    func rebuildLineStarts() {
        let units = (string as NSString)
        var starts = [0]
        starts.reserveCapacity(units.length / 30)
        let length = units.length
        var index = 0
        let buffer = UnsafeMutablePointer<unichar>.allocate(capacity: max(1, length))
        defer { buffer.deallocate() }
        units.getCharacters(buffer, range: NSRange(location: 0, length: length))
        while index < length {
            if buffer[index] == 10 {
                starts.append(index + 1)
            }
            index += 1
        }
        lineStarts = starts
        (enclosingScrollView?.verticalRulerView as? LineGutter)?.updateThickness(lineCount: starts.count)
    }

    /// 1-based line of a UTF-16 offset.
    func lineNumber(at offset: Int) -> Int {
        var low = 0
        var high = lineStarts.count - 1
        while low < high {
            let mid = (low + high + 1) / 2
            if lineStarts[mid] <= offset { low = mid } else { high = mid - 1 }
        }
        return low + 1
    }

    func offset(ofLine line: Int) -> Int {
        lineStarts[max(0, min(lineStarts.count - 1, line - 1))]
    }

    var caretLine: Int { lineNumber(at: selectedRange().location) }

    // MARK: Text changes

    override func didChangeText() {
        super.didChangeText()
        document?.textDidChange()
    }

    var lastEdit: (range: NSRange, delta: Int)?
    var refreshScheduled = false

    /// Called for every character change of the storage (typing, undo, code fixes, reloads).
    func storageEdited(range: NSRange, delta: Int) {
        textStorageEdited(range: range, delta: delta)
        if let previous = lastEdit {
            lastEdit = (NSUnionRange(previous.range, range), previous.delta + delta)
        } else {
            lastEdit = (range, delta)
        }
        guard !refreshScheduled else { return }
        refreshScheduled = true
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated {
                self?.refreshScheduled = false
                self?.documentEdited()
            }
        }
    }

    func textStorageEdited(range: NSRange, delta: Int) {
        let oldLineCount = lineStarts.count
        let editLine = lineNumber(at: range.location)
        rebuildLineStarts()
        let lineDelta = lineStarts.count - oldLineCount
        if lineDelta != 0, let document, !document.breakpoints.isEmpty {
            var moved: [Int: SourceBreakpoint] = [:]
            for (line, breakpoint) in document.breakpoints {
                if line > editLine || (line == editLine && range.location == offset(ofLine: line) && lineDelta > 0) {
                    let target = line + lineDelta
                    if target > editLine || lineDelta > 0 {
                        moved[max(1, target)] = SourceBreakpoint(line: max(1, target), condition: breakpoint.condition)
                    }
                } else {
                    moved[line] = breakpoint
                }
            }
            document.breakpoints = moved
            workspace.debugger.breakpointsChanged(in: document)
        }
    }

    func documentEdited() {
        let edit = lastEdit ?? (NSRange(location: NSNotFound, length: 0), 0)
        lastEdit = nil
        highlighter?.textChanged(editedRange: edit.range, delta: edit.delta)
        updateBracketMatch()
        needsDisplay = true
        enclosingScrollView?.verticalRulerView?.needsDisplay = true
    }

    @objc func scrolled() {
        highlighter?.scheduleSemantic()
        if completion.isVisible || signaturePopup.isVisible {
            positionPopups()
        }
        hoverPopup.dismiss()
    }

    // MARK: Diagnostics

    func setDiagnostics(_ list: [Diagnostic]) {
        guard let layoutManager, let document else { return }
        diagnostics = list
        let full = NSRange(location: 0, length: document.storage.length)
        layoutManager.removeTemporaryAttribute(.underlineStyle, forCharacterRange: full)
        layoutManager.removeTemporaryAttribute(.underlineColor, forCharacterRange: full)
        var byLine: [Int: DiagnosticSeverity] = [:]
        var marks: [MarkerScroller.Mark] = []
        let total = max(1, lineCount)
        for diagnostic in list where diagnostic.severity != .hidden {
            var start = diagnostic.start
            var length = diagnostic.length
            if diagnostic.category == "Build" || (start == 0 && length == 0 && diagnostic.startLine > 1) {
                start = document.offset(line: diagnostic.startLine, column: diagnostic.startColumn)
                length = 0
            }
            if length == 0 {
                // Underline the word at the position so that zero-length diagnostics are visible.
                let word = wordRange(at: start)
                start = word.location
                length = max(1, word.length)
            }
            let range = NSRange(location: min(start, full.length), length: max(0, min(length, full.length - min(start, full.length))))
            if range.length > 0 {
                let color = diagnostic.isStyleCop ? Palette.styleCop : Palette.color(for: diagnostic.severity)
                let style: NSUnderlineStyle = diagnostic.severity == .error ? .thick : .single
                layoutManager.addTemporaryAttribute(.underlineStyle, value: style.rawValue, forCharacterRange: range)
                layoutManager.addTemporaryAttribute(.underlineColor, value: color, forCharacterRange: range)
            }
            let line = diagnostic.startLine
            byLine[line] = max(byLine[line] ?? .hidden, diagnostic.severity)
            if diagnostic.severity >= .warning {
                marks.append(.init(fraction: CGFloat(line - 1) / CGFloat(total), color: diagnostic.isStyleCop ? Palette.styleCop : Palette.color(for: diagnostic.severity)))
            }
        }
        for line in document.breakpoints.keys {
            marks.append(.init(fraction: CGFloat(line - 1) / CGFloat(total), color: Palette.breakpoint))
        }
        diagnosticSeverityByLine = byLine
        (enclosingScrollView?.verticalScroller as? MarkerScroller)?.marks = workspace.preferences.showMinimapOfDiagnostics ? marks : []
        enclosingScrollView?.verticalRulerView?.needsDisplay = true
    }

    func wordRange(at offset: Int) -> NSRange {
        let string = self.string as NSString
        guard offset < string.length else { return NSRange(location: max(0, string.length - 1), length: string.length > 0 ? 1 : 0) }
        var start = offset
        var end = offset
        let isWord: (unichar) -> Bool = { CSharpLexer.isIdentifierPart($0) }
        while start > 0 && isWord(string.character(at: start - 1)) { start -= 1 }
        while end < string.length && isWord(string.character(at: end)) { end += 1 }
        return NSRange(location: start, length: end - start)
    }

    // MARK: Drawing

    override func drawBackground(in rect: NSRect) {
        super.drawBackground(in: rect)
        guard let layoutManager, let document else { return }
        if let line = document.executionLine {
            fillLine(line, color: Palette.executionLine, layoutManager: layoutManager)
        }
        if selectedRange().length == 0, window?.firstResponder === self {
            fillLine(caretLine, color: Palette.currentLine, layoutManager: layoutManager)
        }
    }

    func fillLine(_ line: Int, color: NSColor, layoutManager: NSLayoutManager) {
        let offset = offset(ofLine: line)
        var rect: NSRect
        if offset >= (string as NSString).length && layoutManager.extraLineFragmentRect.height > 0 {
            rect = layoutManager.extraLineFragmentRect
        } else {
            let glyph = layoutManager.glyphIndexForCharacter(at: min(offset, max(0, (string as NSString).length - 1)))
            rect = layoutManager.lineFragmentRect(forGlyphAt: glyph, effectiveRange: nil)
        }
        rect.origin.x = 0
        rect.size.width = bounds.width
        rect.origin.y += textContainerInset.height
        color.setFill()
        rect.fill()
    }

    override func setSelectedRanges(_ ranges: [NSValue], affinity: NSSelectionAffinity, stillSelecting: Bool) {
        let oldLine = caretLine
        super.setSelectedRanges(ranges, affinity: affinity, stillSelecting: stillSelecting)
        if caretLine != oldLine || true {
            needsDisplay = true
            enclosingScrollView?.verticalRulerView?.needsDisplay = true
        }
        if !stillSelecting {
            updateBracketMatch()
            snippet?.selectionChanged(selectedRange())
            if completion.isVisible && (selectedRange().location < completionStart || selectedRange().length > 0) {
                dismissCompletion()
            }
        }
    }

    func updateBracketMatch() {
        guard let layoutManager, let highlighter else { return }
        if let old = bracketHighlight, NSMaxRange(old) <= (string as NSString).length {
            layoutManager.removeTemporaryAttribute(.backgroundColor, forCharacterRange: old)
        }
        bracketHighlight = nil
        let caret = selectedRange().location
        let units = highlighter.units
        for candidate in [caret - 1, caret] where candidate >= 0 && candidate < units.count {
            if let match = TextEditing.matchingBracket(at: candidate, in: units, tokens: highlighter.tokens) {
                for offset in [candidate, match] {
                    layoutManager.addTemporaryAttribute(.backgroundColor, value: Palette.bracketMatch, forCharacterRange: NSRange(location: offset, length: 1))
                }
                bracketHighlight = NSRange(location: min(candidate, match), length: abs(match - candidate) + 1)
                return
            }
        }
    }

    // MARK: Keyboard

    override func keyDown(with event: NSEvent) {
        hoverPopup.dismiss()
        if completion.isVisible {
            switch event.keyCode {
            case 125: completion.move(1); return
            case 126: completion.move(-1); return
            case 121: completion.move(9); return
            case 116: completion.move(-9); return
            case 36, 76, 48:
                if let item = completion.selected {
                    commitCompletion(item)
                    return
                }
            case 53:
                dismissCompletion()
                return
            default:
                break
            }
        }
        if event.keyCode == 53 {
            if signaturePopup.isVisible { signaturePopup.dismiss(); return }
            if snippet != nil { snippet = nil; return }
        }
        super.keyDown(with: event)
    }

    override func insertTab(_ sender: Any?) {
        if let snippet, snippet.next(in: self) {
            return
        }
        snippet = nil
        if expandSnippetBeforeCaret() {
            return
        }
        if selectedRange().length > 0 && (string as NSString).substring(with: selectedRange()).contains("\n") {
            shiftSelectedLines(by: 1)
            return
        }
        insertText(TextEditing.indentUnit, replacementRange: selectedRange())
    }

    override func insertBacktab(_ sender: Any?) {
        if let snippet, snippet.previous(in: self) {
            return
        }
        shiftSelectedLines(by: -1)
    }

    override func insertNewline(_ sender: Any?) {
        snippet = nil
        let string = self.string as NSString
        let result = TextEditing.newline(at: selectedRange().location, in: string)
        let insertAt = selectedRange()
        insertText(result.text, replacementRange: insertAt)
        setSelectedRange(NSRange(location: insertAt.location + result.caret, length: 0))
        scrollRangeToVisible(selectedRange())
    }

    override func deleteBackward(_ sender: Any?) {
        let range = selectedRange()
        let string = self.string as NSString
        if range.length == 0, range.location > 0 {
            // Delete a whole indent unit when the caret sits in leading whitespace.
            let lineStart = string.lineRange(for: NSRange(location: range.location, length: 0)).location
            let before = string.substring(with: NSRange(location: lineStart, length: range.location - lineStart))
            if !before.isEmpty, before.allSatisfy({ $0 == " " }) {
                let remove = (before.count - 1) % TextEditing.indentUnit.count + 1
                insertText("", replacementRange: NSRange(location: range.location - remove, length: remove))
                return
            }
            // Remove an auto-inserted closing pair together with the opening one.
            if range.location < string.length, let closing = TextEditing.closingPair(for: Character(UnicodeScalar(string.character(at: range.location - 1)) ?? " ")),
               String(Character(UnicodeScalar(string.character(at: range.location)) ?? " ")) == String(closing) {
                insertText("", replacementRange: NSRange(location: range.location - 1, length: 2))
                updateCompletionFilter()
                return
            }
        }
        super.deleteBackward(sender)
        updateCompletionFilter()
        if signaturePopup.isVisible { refreshSignatureHelp() }
    }

    override func insertText(_ insertString: Any, replacementRange: NSRange) {
        guard let text = insertString as? String, text.count == 1, let character = text.first else {
            super.insertText(insertString, replacementRange: replacementRange)
            return
        }
        let range = replacementRange.location == NSNotFound ? selectedRange() : replacementRange
        let string = self.string as NSString
        let next: Character? = range.location < string.length ? Character(UnicodeScalar(string.character(at: range.location)) ?? " ") : nil

        // Type over an auto-inserted closing character.
        if range.length == 0, [")", "]", "}", "\"", "'"].contains(character), next == character {
            setSelectedRange(NSRange(location: range.location + 1, length: 0))
            afterTyping(character)
            return
        }
        if workspace.preferences.autoCloseBrackets, let closing = TextEditing.closingPair(for: character), shouldAutoClose(character, next: next, at: range.location) {
            if range.length > 0 {
                let selected = string.substring(with: range)
                super.insertText(String(character) + selected + String(closing), replacementRange: range)
                setSelectedRange(NSRange(location: range.location + 1, length: (selected as NSString).length))
            } else {
                super.insertText(String(character) + String(closing), replacementRange: range)
                setSelectedRange(NSRange(location: range.location + 1, length: 0))
            }
            afterTyping(character)
            return
        }
        // Outdent a closing brace typed on an otherwise empty line.
        if character == "}" {
            let lineStart = string.lineRange(for: NSRange(location: range.location, length: 0)).location
            let before = string.substring(with: NSRange(location: lineStart, length: range.location - lineStart))
            if before.count >= TextEditing.indentUnit.count, before.allSatisfy({ $0 == " " }) {
                super.insertText("}", replacementRange: NSRange(location: range.location - TextEditing.indentUnit.count, length: TextEditing.indentUnit.count + range.length))
                afterTyping(character)
                return
            }
        }
        super.insertText(insertString, replacementRange: replacementRange)
        afterTyping(character)
    }

    func shouldAutoClose(_ character: Character, next: Character?, at location: Int) -> Bool {
        if let next, next.isLetter || next.isNumber || next == "_" {
            return false
        }
        if character == "\"" || character == "'" {
            let tokens = highlighter?.tokens ?? []
            if tokens.contains(where: { ($0.kind == .string || $0.kind == .character || $0.kind == .comment) && location > $0.start && location < $0.start + $0.length }) {
                return false
            }
            if location > 0, let previous = UnicodeScalar((string as NSString).character(at: location - 1)), CharacterSet.alphanumerics.contains(previous) {
                return false
            }
        }
        return true
    }

    func afterTyping(_ character: Character) {
        guard document?.isCSharp == true else { return }
        if completion.isVisible {
            if character.isLetter || character.isNumber || character == "_" {
                updateCompletionFilter()
                return
            }
            if let item = completion.selected, ".;(),[<> =".contains(character), !(completionList?.items.isEmpty ?? true), typedPrefix.count > 0 {
                // Commit characters: keep the typed character after the committed item.
                commitCompletion(item, thenType: character)
                return
            }
            dismissCompletion()
        }
        switch character {
        case "(", ",":
            refreshSignatureHelp()
        case ")":
            signaturePopup.dismiss()
        default:
            break
        }
        if character == "." || character == "<" || character == " " || character.isLetter || character == "_" || character == "#" || character == "[" {
            maybeTriggerCompletion(typed: character)
        }
    }

    func shiftSelectedLines(by direction: Int) {
        let string = self.string as NSString
        let lines = string.lineRange(for: selectedRange())
        let text = string.substring(with: lines)
        var parts = text.components(separatedBy: "\n")
        let trailing = parts.last == ""
        if trailing { parts.removeLast() }
        let shifted = parts.map { line -> String in
            if direction > 0 { return line.isEmpty ? line : TextEditing.indentUnit + line }
            let spaces = line.prefix { $0 == " " }.count
            return String(line.dropFirst(min(spaces, TextEditing.indentUnit.count)))
        }
        let replacement = shifted.joined(separator: "\n") + (trailing ? "\n" : "")
        insertText(replacement, replacementRange: lines)
        setSelectedRange(NSRange(location: lines.location, length: (replacement as NSString).length - (trailing ? 1 : 0)))
    }

    func toggleComment() {
        let result = TextEditing.toggleLineComment(in: string as NSString, range: selectedRange())
        insertText(result.text, replacementRange: result.range)
        setSelectedRange(NSRange(location: result.range.location, length: max(0, (result.text as NSString).length - 1)))
    }

    // MARK: Snippets

    /// Expands the word before the caret when it is a snippet shortcut. Returns true when it did.
    func expandSnippetBeforeCaret() -> Bool {
        guard document?.isCSharp == true, selectedRange().length == 0 else { return false }
        let word = wordRange(at: max(0, selectedRange().location - 1))
        guard NSMaxRange(word) == selectedRange().location, word.length > 0 else { return false }
        let shortcut = (string as NSString).substring(with: word)
        guard let definition = Snippet.find(shortcut) else { return false }
        expand(definition, replacing: word)
        return true
    }

    func expand(_ definition: Snippet, replacing range: NSRange) {
        Task {
            var typeName: String?
            if definition.body.contains("$TYPE"), let engine = workspace.engine, let document {
                workspace.syncNow(document)
                typeName = try? await engine.enclosingType(document.path, offset: range.location)
            }
            let indent = TextEditing.indentation(ofLineAt: range.location, in: string as NSString)
            let expanded = SnippetExpander.expand(definition.body, indentation: indent, typeName: typeName)
            insertText(expanded.text, replacementRange: range)
            let session = SnippetSession(snippet: expanded, base: range.location)
            snippet = session.stops.isEmpty ? nil : session
            if let first = session.firstSelection {
                setSelectedRange(first)
            } else {
                setSelectedRange(NSRange(location: range.location + expanded.finalCaret, length: 0))
            }
        }
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let trackingArea { removeTrackingArea(trackingArea) }
        let area = NSTrackingArea(rect: visibleRect, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect, .mouseEnteredAndExited], owner: self)
        addTrackingArea(area)
        trackingArea = area
    }

    override func mouseMoved(with event: NSEvent) {
        super.mouseMoved(with: event)
        hoverTask?.cancel()
        let point = convert(event.locationInWindow, from: nil)
        guard let offset = characterOffset(at: point) else {
            hoverPopup.dismiss()
            return
        }
        if event.modifierFlags.contains(.command) {
            NSCursor.pointingHand.set()
        }
        hoverTask = Task {
            try? await Task.sleep(for: .milliseconds(450))
            guard !Task.isCancelled else { return }
            await showHover(at: offset, point: event.locationInWindow)
        }
    }

    override func mouseExited(with event: NSEvent) {
        hoverTask?.cancel()
        hoverPopup.dismiss()
    }

    override func mouseDown(with event: NSEvent) {
        hoverPopup.dismiss()
        dismissCompletion()
        if event.modifierFlags.contains(.command), let offset = characterOffset(at: convert(event.locationInWindow, from: nil)), let document {
            setSelectedRange(NSRange(location: offset, length: 0))
            Task { await workspace.goToDefinition(document, offset: offset) }
            return
        }
        super.mouseDown(with: event)
    }


    override func menu(for event: NSEvent) -> NSMenu? {
        let menu = NSMenu()
        if let offset = characterOffset(at: convert(event.locationInWindow, from: nil)), selectedRange().length == 0 || !NSLocationInRange(offset, selectedRange()) {
            setSelectedRange(NSRange(location: offset, length: 0))
        }
        func item(_ title: String, _ symbol: String, _ key: String = "", _ modifiers: NSEvent.ModifierFlags = [], action: @escaping () -> Void) {
            let entry = QuickActionItem(title: title, action: #selector(QuickActionItem.run), keyEquivalent: key)
            entry.keyEquivalentModifierMask = modifiers
            entry.target = entry
            entry.handler = action
            entry.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
            menu.addItem(entry)
        }
        if document?.isCSharp == true {
            item(String(localized: "Quick Actions and Fixes…"), "lightbulb", ".", [.command, .shift]) { [weak self] in self?.showQuickActions() }
            menu.addItem(.separator())
            item(String(localized: "Go to Definition"), "arrow.right.circle") { [weak self] in self?.goToDefinition() }
            item(String(localized: "Find References"), "magnifyingglass") { [weak self] in self?.findReferences() }
            item(String(localized: "Rename Symbol…"), "pencil") { [weak self] in self?.renameSymbol() }
            menu.addItem(.separator())
            item(String(localized: "Format Document"), "text.alignleft") { [weak self] in
                guard let self, let document = self.document else { return }
                Task { await self.workspace.formatDocument(document) }
            }
            item(String(localized: "Toggle Comment"), "text.badge.minus", "/", .command) { [weak self] in self?.toggleComment() }
            item(String(localized: "Toggle Breakpoint"), "circle.fill") { [weak self] in self?.toggleBreakpointAtCaret() }
            menu.addItem(.separator())
        }
        menu.addItem(withTitle: String(localized: "Cut"), action: #selector(cut(_:)), keyEquivalent: "x")
        menu.addItem(withTitle: String(localized: "Copy"), action: #selector(copy(_:)), keyEquivalent: "c")
        menu.addItem(withTitle: String(localized: "Paste"), action: #selector(paste(_:)), keyEquivalent: "v")
        return menu
    }



    override func becomeFirstResponder() -> Bool {
        let result = super.becomeFirstResponder()
        workspace.activeEditor = self
        needsDisplay = true
        return result
    }

    override func resignFirstResponder() -> Bool {
        dismissCompletion()
        signaturePopup.dismiss()
        hoverPopup.dismiss()
        needsDisplay = true
        return super.resignFirstResponder()
    }
}

extension CodeTextView: NSTextStorageDelegate {
    nonisolated func textStorage(_ textStorage: NSTextStorage, didProcessEditing editedMask: NSTextStorageEditActions, range editedRange: NSRange, changeInLength delta: Int) {
        guard editedMask.contains(.editedCharacters) else { return }
        MainActor.assumeIsolated {
            storageEdited(range: editedRange, delta: delta)
        }
    }
}

extension CodeTextView: NSTextViewDelegate {
    func undoManager(for view: NSTextView) -> UndoManager? { document?.undoManager }

    func textView(_ textView: NSTextView, shouldChangeTextIn affectedCharRange: NSRange, replacementString: String?) -> Bool {
        if let replacementString {
            snippet?.textWillChange(in: affectedCharRange, replacement: replacementString)
        }
        return true
    }
}

/// A menu item that runs a closure.
final class QuickActionItem: NSMenuItem {
    var handler: (() -> Void)?

    @objc func run() {
        handler?()
    }
}

