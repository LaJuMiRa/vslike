import AppKit
import SwiftUI
import VsLikeKit

/// Completion, signature help and hover of the code editor.
extension CodeTextView {
    // MARK: Completion

    var typedPrefix: String {
        let caret = selectedRange().location
        guard caret >= completionStart else { return "" }
        return (string as NSString).substring(with: NSRange(location: completionStart, length: caret - completionStart))
    }

    func triggerCompletion() {
        requestCompletion()
    }

    func maybeTriggerCompletion(typed: Character) {
        guard let engine = workspace.engine, workspace.isEngineReady, let document else { return }
        let offset = selectedRange().location
        completionTask?.cancel()
        completionTask = Task {
            try? await Task.sleep(for: .milliseconds(typed == "." ? 0 : 40))
            guard !Task.isCancelled else { return }
            workspace.syncNow(document)
            // Only the first letter of a word opens the list; later letters filter it.
            if typed.isLetter || typed == "_" {
                let word = wordRange(at: max(0, offset - 1))
                guard word.length == 1 else { return }
            }
            guard (try? await engine.shouldTriggerCompletion(document.path, offset: offset, typed: typed)) == true else { return }
            await loadCompletion(at: offset)
        }
    }

    func requestCompletion() {
        completionTask?.cancel()
        completionTask = Task { await loadCompletion(at: selectedRange().location) }
    }

    func loadCompletion(at offset: Int) async {
        guard let engine = workspace.engine, workspace.isEngineReady, let document else { return }
        workspace.syncNow(document)
        guard let list = try? await engine.completion(document.path, offset: offset), !Task.isCancelled, selectedRange().location >= list.spanStart else {
            return
        }
        var items = list.items
        let prefixStart = list.spanStart
        let prefix = (string as NSString).substring(with: NSRange(location: prefixStart, length: max(0, selectedRange().location - prefixStart)))
        let snippets = Snippet.csharp.filter { !prefix.isEmpty && $0.shortcut.hasPrefix(prefix.lowercased()) }.enumerated().map { index, snippet in
            CompletionItem(displayText: snippet.shortcut, filterText: snippet.shortcut, sortText: snippet.shortcut, kind: .snippet, inlineDescription: snippet.title, handle: -(index + 1))
        }
        items = snippets + items
        completionList = CompletionList(items: items, spanStart: list.spanStart, spanLength: list.spanLength)
        completionStart = list.spanStart
        updateCompletionFilter(show: true)
    }

    func updateCompletionFilter(show: Bool = false) {
        guard let list = completionList, show || completion.isVisible else { return }
        let caret = selectedRange().location
        guard caret >= completionStart else {
            dismissCompletion()
            return
        }
        let prefix = typedPrefix
        let filtered: [CompletionItem]
        if prefix.isEmpty {
            filtered = list.items.sorted { $0.sortText.localizedCaseInsensitiveCompare($1.sortText) == .orderedAscending }
        } else {
            let scored = list.items.compactMap { item -> (CompletionItem, Int)? in
                guard let score = FuzzyMatcher.score(prefix, item.filterText) else { return nil }
                let exactPrefix = item.filterText.lowercased().hasPrefix(prefix.lowercased()) ? 100 : 0
                let caseBonus = item.filterText.hasPrefix(prefix) ? 20 : 0
                return (item, score + exactPrefix + caseBonus)
            }
            filtered = scored.sorted { $0.1 != $1.1 ? $0.1 > $1.1 : $0.0.sortText < $1.0.sortText }.map(\.0)
        }
        if filtered.isEmpty || (filtered.count == 1 && filtered[0].filterText == prefix && filtered[0].kind != .snippet) {
            dismissCompletion()
            return
        }
        guard let window, let anchor = screenRect(at: completionStart) else { return }
        completion.show(Array(filtered.prefix(400)), near: anchor, in: window)
    }

    func dismissCompletion() {
        completion.dismiss()
        completionList = nil
        detailTask?.cancel()
    }

    func loadCompletionDetail(_ item: CompletionItem) {
        detailTask?.cancel()
        guard item.handle >= 0, let engine = workspace.engine, let document else {
            completion.setDetail(item.kind == .snippet ? Snippet.find(item.displayText)?.body.replacingOccurrences(of: "\t", with: "    ") : nil)
            return
        }
        detailTask = Task {
            try? await Task.sleep(for: .milliseconds(120))
            guard !Task.isCancelled else { return }
            let text = try? await engine.completionDescription(document.path, handle: item.handle)
            guard !Task.isCancelled, completion.isVisible else { return }
            completion.setDetail(text)
        }
    }

    func commitCompletion(_ item: CompletionItem, thenType character: Character? = nil) {
        let start = completionStart
        let end = selectedRange().location
        dismissCompletion()
        if item.kind == .snippet, let definition = Snippet.find(item.displayText) {
            expand(definition, replacing: NSRange(location: start, length: end - start))
            return
        }
        guard let engine = workspace.engine, let document else { return }
        workspace.syncNow(document)
        Task {
            if let edit = try? await engine.resolveCompletion(document.path, handle: item.handle), let file = edit.files.first(where: { RealPath.resolve($0.filePath) == document.path }), !file.edits.isEmpty {
                undoManager?.beginUndoGrouping()
                var caretShift = 0
                var caret = end
                for change in TextEditing.completionEdits(file.edits, spanStart: start, caret: end).sorted(by: { $0.start > $1.start }) {
                    let range = NSRange(location: change.start, length: change.length)
                    if shouldChangeText(in: range, replacementString: change.newText) {
                        textStorage?.replaceCharacters(in: range, with: change.newText)
                        didChangeText()
                    }
                    let delta = (change.newText as NSString).length - change.length
                    if change.start + change.length <= end - (end - start) {
                        caretShift += delta
                    }
                    if change.start <= start && change.start + change.length >= start {
                        caret = change.start + (change.newText as NSString).length
                    }
                }
                undoManager?.endUndoGrouping()
                if let target = edit.caretOffset, edit.caretFile.map(RealPath.resolve) == document.path {
                    setSelectedRange(NSRange(location: target, length: 0))
                } else {
                    setSelectedRange(NSRange(location: caret + caretShift, length: 0))
                }
            } else {
                insertText(item.displayText, replacementRange: NSRange(location: start, length: end - start))
            }
            if let character {
                insertText(String(character), replacementRange: selectedRange())
            } else if item.kind == .method || item.kind == .extensionMethod {
                refreshSignatureHelp()
            }
        }
    }

    // MARK: Signature help

    func showSignatureHelp() {
        refreshSignatureHelp()
    }

    func refreshSignatureHelp() {
        guard let engine = workspace.engine, workspace.isEngineReady, let document else { return }
        signatureTask?.cancel()
        let offset = selectedRange().location
        signatureTask = Task {
            try? await Task.sleep(for: .milliseconds(30))
            workspace.syncNow(document)
            guard !Task.isCancelled else { return }
            let help = try? await engine.signatureHelp(document.path, offset: offset)
            // A newer refresh or a dismissal took over meanwhile, or the caret has moved on.
            guard !Task.isCancelled, selectedRange().location == offset else { return }
            guard let help, !help.signatures.isEmpty, let window, window.firstResponder === self, let anchor = screenRect(at: offset) else {
                signaturePopup.dismiss()
                return
            }
            signaturePopup.show(SignatureHelpCard(help: help), near: anchor, in: window, above: true)
        }
    }

    func dismissSignatureHelp() {
        signatureTask?.cancel()
        signaturePopup.dismiss()
    }

    func positionPopups() {
        if completion.isVisible { updateCompletionFilter() }
        if signaturePopup.isVisible { refreshSignatureHelp() }
    }

    // MARK: Hover

    func showHover(at offset: Int, point: NSPoint) async {
        guard let document, let window else { return }
        let anchor = window.convertToScreen(NSRect(origin: point, size: NSSize(width: 1, height: 1))).offsetBy(dx: 0, dy: -8)
        if workspace.debugger.isStopped {
            let word = expression(at: offset)
            if let value = await workspace.debugger.hoverValue(word), !Task.isCancelled {
                hoverPopup.show(Text(value).font(.system(size: 12, design: .monospaced)).textSelection(.enabled), near: anchor, in: window, above: false)
                return
            }
        }
        let local = diagnostics.filter { $0.start <= offset && offset <= $0.start + max($0.length, 1) && $0.severity != .hidden }
        var signature = ""
        var documentation: String?
        if let engine = workspace.engine, workspace.isEngineReady, document.isCSharp {
            workspace.syncNow(document)
            if let info = try? await engine.quickInfo(document.path, offset: offset) {
                signature = info.signature
                documentation = info.documentation
            }
        }
        guard !Task.isCancelled, !signature.isEmpty || !local.isEmpty else { return }
        hoverPopup.show(QuickInfoCard(signature: signature, documentation: documentation, diagnostics: local), near: anchor, in: window, above: false)
    }

    /// The identifier chain under the mouse (`car.Name`) for debugger hover.
    func expression(at offset: Int) -> String {
        let string = self.string as NSString
        var start = offset
        var end = offset
        let isPart: (unichar) -> Bool = { CSharpLexer.isIdentifierPart($0) }
        while start > 0 && (isPart(string.character(at: start - 1)) || string.character(at: start - 1) == 46) { start -= 1 }
        while end < string.length && isPart(string.character(at: end)) { end += 1 }
        return string.substring(with: NSRange(location: start, length: end - start))
    }

    func characterOffset(at point: NSPoint) -> Int? {
        guard let layoutManager, let textContainer else { return nil }
        let containerPoint = NSPoint(x: point.x - textContainerInset.width, y: point.y - textContainerInset.height)
        var fraction: CGFloat = 0
        let glyph = layoutManager.glyphIndex(for: containerPoint, in: textContainer, fractionOfDistanceThroughGlyph: &fraction)
        let rect = layoutManager.boundingRect(forGlyphRange: NSRange(location: glyph, length: 1), in: textContainer)
        guard rect.insetBy(dx: -2, dy: -2).contains(containerPoint) else { return nil }
        return layoutManager.characterIndexForGlyph(at: glyph)
    }

    func screenRect(at offset: Int) -> NSRect? {
        guard let window else { return nil }
        var actual = NSRange()
        let rect = firstRect(forCharacterRange: NSRange(location: min(offset, (string as NSString).length), length: 0), actualRange: &actual)
        guard rect.width.isFinite else { return nil }
        _ = window
        return rect
    }
}
