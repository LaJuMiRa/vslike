import AppKit
import SwiftUI
import VsLikeKit

/// Quick actions (⌘⇧.), navigation, refactoring, breakpoints and the context menu of the code editor.
extension CodeTextView {
    // MARK: Quick actions (⌘⇧.)

    func showQuickActions() {
        guard let document else { return }
        dismissCompletion()
        let range = selectedRange()
        Task {
            let actions = await workspace.codeActions(document, range: range)
            presentQuickActions(actions, at: range.location)
        }
    }

    func presentQuickActions(_ actions: [CodeAction], at offset: Int) {
        let menu = NSMenu(title: String(localized: "Quick Actions"))
        menu.autoenablesItems = false
        let fixes = actions.filter { $0.isFix && !$0.isFixAll }
        let fixAll = actions.filter(\.isFixAll)
        let refactorings = actions.filter { !$0.isFix }
        func add(_ action: CodeAction, symbol: String, tint: NSColor) {
            let item = QuickActionItem(title: action.title, action: #selector(QuickActionItem.run), keyEquivalent: "")
            item.target = item
            item.handler = { [weak self] in
                guard let self else { return }
                Task { await self.workspace.perform(action) }
            }
            item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?.withSymbolConfiguration(.init(paletteColors: [tint]))
            if action.isFix, action.group.count > 2 {
                item.badge = NSMenuItemBadge(string: action.group)
            }
            menu.addItem(item)
        }
        if actions.isEmpty {
            let empty = NSMenuItem(title: String(localized: "No quick actions here"), action: nil, keyEquivalent: "")
            empty.isEnabled = false
            menu.addItem(empty)
        }
        if !fixes.isEmpty {
            menu.addItem(.sectionHeader(title: String(localized: "Fixes")))
            fixes.forEach { add($0, symbol: $0.group.hasPrefix("SA") ? "checkmark.seal" : "lightbulb.fill", tint: $0.group.hasPrefix("SA") ? Palette.styleCop : Palette.warning) }
        }
        if !fixAll.isEmpty {
            menu.addItem(.sectionHeader(title: String(localized: "Fix All")))
            fixAll.forEach { add($0, symbol: "wand.and.stars", tint: Palette.styleCop) }
        }
        if !refactorings.isEmpty {
            menu.addItem(.sectionHeader(title: String(localized: "Refactorings")))
            refactorings.prefix(40).forEach { add($0, symbol: "hammer", tint: Palette.accent) }
        }
        guard let rect = screenRect(at: offset), let window else { return }
        let windowPoint = window.convertPoint(fromScreen: NSPoint(x: rect.minX, y: rect.minY - 4))
        menu.popUp(positioning: nil, at: convert(windowPoint, from: nil), in: self)
    }

    // MARK: Navigation and refactoring

    func goToDefinition() {
        guard let document else { return }
        Task { await workspace.goToDefinition(document, offset: selectedRange().location) }
    }

    func findReferences() {
        guard let document else { return }
        Task { await workspace.findReferences(document, offset: selectedRange().location) }
    }

    func renameSymbol() {
        guard let document else { return }
        Task { await workspace.rename(document, offset: selectedRange().location) }
    }

    func promptGoToLine() {
        workspace.prompt = PromptRequest(title: String(localized: "Go to Line"), message: String(localized: "Line 1 – \(lineCount)"), placeholder: "42", confirmTitle: String(localized: "Go")) { [weak self] text in
            guard let self, let line = Int(text.trimmingCharacters(in: .whitespaces)) else { return }
            self.reveal(NSRange(location: self.offset(ofLine: line), length: 0))
        }
    }

    func reveal(_ range: NSRange) {
        let length = (string as NSString).length
        let clamped = NSRange(location: min(range.location, length), length: min(range.length, length - min(range.location, length)))
        setSelectedRange(clamped)
        scrollRangeToVisible(clamped)
        if let layoutManager, let textContainer {
            let glyphs = layoutManager.glyphRange(forCharacterRange: clamped, actualCharacterRange: nil)
            var rect = layoutManager.boundingRect(forGlyphRange: glyphs, in: textContainer)
            rect.origin.y -= visibleRect.height / 3
            rect.size.height = visibleRect.height * 2 / 3
            scrollToVisible(rect)
        }
        showFindIndicator(for: clamped)
    }

    // MARK: Breakpoints

    func toggleBreakpointAtCaret() {
        toggleBreakpoint(line: caretLine)
    }

    func toggleBreakpoint(line: Int) {
        guard let document else { return }
        document.toggleBreakpoint(line: line)
        workspace.debugger.breakpointsChanged(in: document)
        enclosingScrollView?.verticalRulerView?.needsDisplay = true
        setDiagnostics(diagnostics)
    }

    func editBreakpointCondition(line: Int) {
        guard let document else { return }
        workspace.prompt = PromptRequest(
            title: String(localized: "Breakpoint Condition"),
            message: String(localized: "Line \(line). The program stops only when the expression is true; leave empty to always stop."),
            placeholder: "count > 10",
            initialValue: document.breakpoints[line]?.condition ?? "",
            confirmTitle: String(localized: "Set")
        ) { [weak self] condition in
            guard let self else { return }
            document.breakpoints[line] = SourceBreakpoint(line: line, condition: condition.isEmpty ? nil : condition)
            self.workspace.debugger.breakpointsChanged(in: document)
            self.enclosingScrollView?.verticalRulerView?.needsDisplay = true
        }
    }

    // MARK: Context menu
}
