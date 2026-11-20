import SwiftUI
import VsLikeKit

/// A command reachable from the palette.
struct PaletteCommand: Identifiable {
    let id: String
    let title: String
    let category: String
    let symbol: String
    let shortcut: String?
    let action: @MainActor () -> Void
}

/// Spotlight-like overlay for commands (⇧⌘P) and files (⌘P).
struct CommandPalette: View {
    enum Mode { case commands, files }

    @Environment(Workspace.self) private var workspace
    let mode: Mode
    @State private var query = ""
    @State private var selection = 0
    @FocusState private var focused: Bool

    private struct Entry: Identifiable {
        let id: String
        let title: String
        let detail: String
        let symbol: String
        let shortcut: String?
        let action: @MainActor () -> Void
    }

    private var entries: [Entry] {
        let all: [Entry]
        switch mode {
        case .commands:
            all = commands.map { Entry(id: $0.id, title: $0.title, detail: $0.category, symbol: $0.symbol, shortcut: $0.shortcut, action: $0.action) }
        case .files:
            let root = workspace.rootURL?.path ?? ""
            all = FileTreeBuilder.allFiles(workspace.folderTree).compactMap { node in
                guard let url = node.url else { return nil }
                let relative = url.path.hasPrefix(root + "/") ? String(url.path.dropFirst(root.count + 1)) : url.path
                return Entry(id: url.path, title: url.lastPathComponent, detail: relative, symbol: FileNode.symbol(forFile: url.lastPathComponent), shortcut: nil) {
                    workspace.openFile(url)
                }
            }
        }
        guard !query.isEmpty else {
            return Array(all.prefix(200))
        }
        return all.compactMap { entry -> (Entry, Int)? in
            let score = max(FuzzyMatcher.score(query, entry.title) ?? Int.min, (FuzzyMatcher.score(query, entry.detail) ?? Int.min) - 15)
            return score == Int.min ? nil : (entry, score)
        }
        .sorted { $0.1 > $1.1 }
        .prefix(200)
        .map(\.0)
    }

    var body: some View {
        let list = entries
        ZStack(alignment: .top) {
            Color.black.opacity(0.12)
                .ignoresSafeArea()
                .onTapGesture { dismiss() }
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    Image(systemName: mode == .commands ? "command" : "doc.text.magnifyingglass")
                        .foregroundStyle(Color.vsAccent)
                        .font(.system(size: 16, weight: .medium))
                    TextField(mode == .commands ? "Type a command" : "Go to file", text: $query)
                        .textFieldStyle(.plain)
                        .font(.system(size: 17))
                        .focused($focused)
                        .onSubmit { run(list) }
                        .onKeyPress(.downArrow) {
                            selection = min(list.count - 1, selection + 1)
                            return .handled
                        }
                        .onKeyPress(.upArrow) {
                            selection = max(0, selection - 1)
                            return .handled
                        }
                        .onKeyPress(.escape) {
                            dismiss()
                            return .handled
                        }
                }
                .padding(.horizontal, 16)
                .frame(height: 50)
                Divider()
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 2) {
                            ForEach(Array(list.enumerated()), id: \.element.id) { index, entry in
                                PaletteRow(title: entry.title, detail: entry.detail, symbol: entry.symbol, shortcut: entry.shortcut, isSelected: index == selection)
                                    .id(index)
                                    .onTapGesture {
                                        selection = index
                                        run(list)
                                    }
                            }
                        }
                        .padding(6)
                    }
                    .frame(maxHeight: 380)
                    .onChange(of: selection) { _, value in proxy.scrollTo(value) }
                }
                if list.isEmpty {
                    Text("No results").foregroundStyle(.secondary).padding(20)
                }
            }
            .frame(width: 620)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(.separator.opacity(0.6)))
            .shadow(color: .black.opacity(0.28), radius: 30, y: 16)
            .padding(.top, 80)
        }
        .onAppear { focused = true }
        .onChange(of: query) { selection = 0 }
        .transition(.opacity.combined(with: .scale(scale: 0.98)))
    }

    private func run(_ list: [Entry]) {
        guard list.indices.contains(selection) else { return }
        let action = list[selection].action
        dismiss()
        DispatchQueue.main.async { action() }
    }

    private func dismiss() {
        workspace.isCommandPalettePresented = false
        workspace.isQuickOpenPresented = false
    }

    private var commands: [PaletteCommand] {
        let w = workspace
        let editor = w.activeEditor
        var list: [PaletteCommand] = [
            PaletteCommand(id: "build", title: String(localized: "Build Solution"), category: String(localized: "Build"), symbol: "hammer", shortcut: "⌘B") { w.build.start(.build) },
            PaletteCommand(id: "rebuild", title: String(localized: "Rebuild Solution"), category: String(localized: "Build"), symbol: "arrow.triangle.2.circlepath", shortcut: "⇧⌘B") { w.build.start(.rebuild) },
            PaletteCommand(id: "clean", title: String(localized: "Clean Solution"), category: String(localized: "Build"), symbol: "trash", shortcut: "⇧⌘K") { w.build.start(.clean) },
            PaletteCommand(id: "run", title: String(localized: "Run with Debugger"), category: String(localized: "Debug"), symbol: "play.fill", shortcut: "⌘R") { Task { await w.debugger.start() } },
            PaletteCommand(id: "runNoDebug", title: String(localized: "Run Without Debugging"), category: String(localized: "Build"), symbol: "play", shortcut: "⌃⌘R") { w.build.startWithoutDebugging() },
            PaletteCommand(id: "tests", title: String(localized: "Run All Tests"), category: String(localized: "Tests"), symbol: "checkmark.diamond", shortcut: "⌘U") { w.tests.run() },
            PaletteCommand(id: "diagram", title: String(localized: "Open Class Diagram"), category: String(localized: "View"), symbol: "rectangle.3.group", shortcut: "⇧⌘D") { Task { await w.openSolutionDiagram() } },
            PaletteCommand(id: "terminal", title: String(localized: "New Terminal"), category: String(localized: "View"), symbol: "terminal", shortcut: "⌃`") { w.terminals.newShell() },
            PaletteCommand(id: "problems", title: String(localized: "Show Problems"), category: String(localized: "View"), symbol: "exclamationmark.bubble", shortcut: "⇧⌘M") { w.show(.problems) },
            PaletteCommand(id: "output", title: String(localized: "Show Output"), category: String(localized: "View"), symbol: "text.alignleft", shortcut: nil) { w.show(.output) },
            PaletteCommand(id: "stylecop", title: w.styleCopEnabled ? String(localized: "Turn StyleCop Off") : String(localized: "Turn StyleCop On"), category: "StyleCop", symbol: "checkmark.seal", shortcut: nil) { Task { await w.toggleStyleCop() } },
            PaletteCommand(id: "newFile", title: String(localized: "New File…"), category: String(localized: "File"), symbol: "doc.badge.plus", shortcut: "⌘N") { w.promptNewFile(in: nil) },
            PaletteCommand(id: "newSolution", title: String(localized: "New Solution…"), category: String(localized: "File"), symbol: "plus.square.on.square", shortcut: "⇧⌘N") { w.isNewSolutionPresented = true },
            PaletteCommand(id: "addProject", title: String(localized: "Add Project…"), category: String(localized: "File"), symbol: "shippingbox", shortcut: nil) { w.newProjectTarget = w.solution },
            PaletteCommand(id: "open", title: String(localized: "Open…"), category: String(localized: "File"), symbol: "folder", shortcut: "⌘O") { OpenPanel.chooseAndOpen(w) },
            PaletteCommand(id: "saveAll", title: String(localized: "Save All"), category: String(localized: "File"), symbol: "square.and.arrow.down.on.square", shortcut: "⌥⌘S") { w.saveAll() },
            PaletteCommand(id: "restart", title: String(localized: "Restart Analysis"), category: String(localized: "Code"), symbol: "arrow.clockwise", shortcut: nil) { Task { await w.restartEngine() } },
            PaletteCommand(id: "settings", title: String(localized: "Settings…"), category: "VsLike", symbol: "gearshape", shortcut: "⌘,") {
                NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
            },
        ]
        if let editor, let document = w.activeDocument {
            list += [
                PaletteCommand(id: "quick", title: String(localized: "Quick Actions and Fixes"), category: String(localized: "Code"), symbol: "lightbulb", shortcut: "⇧⌘.") { editor.showQuickActions() },
                PaletteCommand(id: "fixStyle", title: String(localized: "Fix All StyleCop Issues in File"), category: "StyleCop", symbol: "wand.and.stars", shortcut: "⌥⇧⌘.") { Task { await w.fixAllStyleCop(in: document) } },
                PaletteCommand(id: "format", title: String(localized: "Format Document"), category: String(localized: "Code"), symbol: "text.alignleft", shortcut: "⌥⇧F") { Task { await w.formatDocument(document) } },
                PaletteCommand(id: "rename", title: String(localized: "Rename Symbol…"), category: String(localized: "Code"), symbol: "pencil", shortcut: "F2") { editor.renameSymbol() },
                PaletteCommand(id: "definition", title: String(localized: "Go to Definition"), category: String(localized: "Navigate"), symbol: "arrow.right.circle", shortcut: "F12") { editor.goToDefinition() },
                PaletteCommand(id: "references", title: String(localized: "Find References"), category: String(localized: "Navigate"), symbol: "magnifyingglass", shortcut: "⇧F12") { editor.findReferences() },
                PaletteCommand(id: "line", title: String(localized: "Go to Line…"), category: String(localized: "Navigate"), symbol: "number", shortcut: "⌘L") { editor.promptGoToLine() },
                PaletteCommand(id: "comment", title: String(localized: "Toggle Comment"), category: String(localized: "Code"), symbol: "text.badge.minus", shortcut: "⌘/") { editor.toggleComment() },
                PaletteCommand(id: "breakpoint", title: String(localized: "Toggle Breakpoint"), category: String(localized: "Debug"), symbol: "circle.fill", shortcut: "⌘\\") { editor.toggleBreakpointAtCaret() },
            ]
        }
        return list
    }
}

private struct PaletteRow: View {
    let title: String
    let detail: String
    let symbol: String
    let shortcut: String?
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: symbol)
                .frame(width: 20)
                .foregroundStyle(isSelected ? .white : Color.vsAccent)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).lineLimit(1)
                if !detail.isEmpty {
                    Text(detail).font(.caption).foregroundStyle(isSelected ? .white.opacity(0.8) : .secondary).lineLimit(1).truncationMode(.middle)
                }
            }
            Spacer()
            if let shortcut {
                Text(shortcut).font(.caption).foregroundStyle(isSelected ? .white.opacity(0.85) : .secondary)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .foregroundStyle(isSelected ? .white : .primary)
        .background(isSelected ? Color.vsAccent : .clear, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .contentShape(Rectangle())
    }
}
