import AppKit
import SwiftUI
import VsLikeKit

/// Problems, output, terminal and debugger below the editor.
struct BottomPanel: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        @Bindable var workspace = workspace
        VStack(spacing: 0) {
            HStack(spacing: 4) {
                PanelTabButton(title: "Problems", symbol: "exclamationmark.bubble", tab: .problems, badge: workspace.diagnostics.all.count)
                PanelTabButton(title: "Output", symbol: "text.alignleft", tab: .output, badge: 0)
                PanelTabButton(title: "Terminal", symbol: "terminal", tab: .terminal, badge: workspace.terminals.sessions.count)
                if workspace.debugger.isActive || !workspace.debugger.console.isEmpty {
                    PanelTabButton(title: "Debug", symbol: "ladybug", tab: .debug, badge: 0)
                }
                Spacer()
                PanelAccessory()
                Button {
                    withAnimation(.snappy) { workspace.isBottomPanelVisible = false }
                } label: {
                    Image(systemName: "chevron.down")
                }
                .buttonStyle(.borderless)
                .help("Hide Panel (⌘J)")
            }
            .padding(.horizontal, 10)
            .frame(height: 34)
            .background(.bar)
            .overlay(alignment: .top) { Divider() }
            .overlay(alignment: .bottom) { Divider().opacity(0.6) }

            Group {
                switch workspace.bottomTab {
                case .problems: ProblemsView()
                case .output: OutputPanel()
                case .terminal: TerminalPanel()
                case .debug: DebugPanel()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

private struct PanelTabButton: View {
    @Environment(Workspace.self) private var workspace
    let title: LocalizedStringKey
    let symbol: String
    let tab: BottomTab
    let badge: Int

    var body: some View {
        let isSelected = workspace.bottomTab == tab
        Button {
            workspace.bottomTab = tab
        } label: {
            HStack(spacing: 5) {
                Image(systemName: symbol)
                Text(title)
                if badge > 0 {
                    Text("\(badge)")
                        .font(.system(size: 10, weight: .semibold).monospacedDigit())
                        .padding(.horizontal, 5)
                        .padding(.vertical, 1)
                        .background(isSelected ? Color.vsAccent.opacity(0.25) : Color.secondary.opacity(0.18), in: Capsule())
                }
            }
            .font(.system(size: 12, weight: isSelected ? .semibold : .regular))
            .foregroundStyle(isSelected ? .primary : .secondary)
            .padding(.horizontal, 10)
            .frame(height: 24)
            .background(isSelected ? Color.primary.opacity(0.08) : .clear, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Filters and actions that belong to the selected panel tab.
private struct PanelAccessory: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        @Bindable var diagnostics = workspace.diagnostics
        @Bindable var output = workspace.output
        switch workspace.bottomTab {
        case .problems:
            HStack(spacing: 8) {
                Picker("", selection: $diagnostics.filter) {
                    Text("All").tag(DiagnosticsStore.Filter.all)
                    Text("Errors \(workspace.diagnostics.errorCount)").tag(DiagnosticsStore.Filter.errors)
                    Text("Warnings").tag(DiagnosticsStore.Filter.warnings)
                    Text("StyleCop \(workspace.diagnostics.styleCopCount)").tag(DiagnosticsStore.Filter.styleCop)
                }
                .pickerStyle(.segmented)
                .controlSize(.small)
                .fixedSize()
                TextField("Filter", text: $diagnostics.search)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.small)
                    .frame(width: 160)
            }
        case .output:
            HStack(spacing: 8) {
                Picker("", selection: $output.selectedChannel) {
                    ForEach(OutputLog.Channel.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .controlSize(.small)
                .fixedSize()
                Button { workspace.output.clear(workspace.output.selectedChannel) } label: { Image(systemName: "trash") }
                    .buttonStyle(.borderless)
                    .help("Clear")
            }
        case .terminal:
            Button { workspace.terminals.newShell() } label: { Image(systemName: "plus") }
                .buttonStyle(.borderless)
                .help("New Terminal")
        case .debug:
            EmptyView()
        }
    }
}

/// The problem list: severity, code, message, file and line. Double-click (or Return) opens the location.
struct ProblemsView: View {
    @Environment(Workspace.self) private var workspace
    @State private var selection: Diagnostic.ID?

    var body: some View {
        let problems = workspace.diagnostics.visible
        if problems.isEmpty {
            ContentUnavailableView {
                Label(workspace.diagnostics.all.isEmpty ? "No problems" : "No matching problems", systemImage: "checkmark.seal")
            } description: {
                Text(workspace.diagnostics.all.isEmpty ? "Errors, warnings and StyleCop findings appear here while you type." : "Change the filter to see more.")
            }
        } else {
            Table(problems, selection: $selection) {
                TableColumn("") { diagnostic in
                    SeverityIcon(diagnostic: diagnostic)
                }
                .width(22)
                TableColumn("Code") { diagnostic in
                    CodeBadge(diagnostic: diagnostic)
                }
                .width(min: 60, ideal: 74, max: 100)
                TableColumn("Message") { diagnostic in
                    Text(diagnostic.message).lineLimit(2).help(diagnostic.message)
                }
                TableColumn("File") { diagnostic in
                    if let file = diagnostic.filePath {
                        Text("\(URL(fileURLWithPath: file).lastPathComponent):\(diagnostic.startLine)")
                            .foregroundStyle(.secondary)
                            .help(file)
                    }
                }
                .width(min: 120, ideal: 180, max: 320)
                TableColumn("Project") { diagnostic in
                    Text(diagnostic.projectName ?? workspace.project(containing: diagnostic.filePath ?? "")?.name ?? "")
                        .foregroundStyle(.secondary)
                }
                .width(min: 60, ideal: 100, max: 180)
            }
            .tableStyle(.inset(alternatesRowBackgrounds: true))
            .font(.system(size: 12))
            .contextMenu(forSelectionType: Diagnostic.ID.self) { ids in
                if let id = ids.first, let diagnostic = problems.first(where: { $0.id == id }) {
                    Button("Go to Location") { workspace.navigate(to: diagnostic) }
                    Button("Quick Actions and Fixes…") {
                        workspace.navigate(to: diagnostic)
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { workspace.activeEditor?.showQuickActions() }
                    }
                    Button("Copy Message") {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString("\(diagnostic.code): \(diagnostic.message)", forType: .string)
                    }
                    if let link = diagnostic.helpLink, let url = URL(string: link) {
                        Button("Open Documentation") { NSWorkspace.shared.open(url) }
                    }
                }
            } primaryAction: { ids in
                if let id = ids.first, let diagnostic = problems.first(where: { $0.id == id }) {
                    workspace.navigate(to: diagnostic)
                }
            }
        }
    }
}

struct SeverityIcon: View {
    let diagnostic: Diagnostic

    var body: some View {
        switch diagnostic.severity {
        case .error: Image(systemName: "xmark.octagon.fill").foregroundStyle(Color.vsError)
        case .warning:
            Image(systemName: diagnostic.isStyleCop ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                .foregroundStyle(diagnostic.isStyleCop ? Color.vsStyleCop : Color.vsWarning)
        case .info, .hidden: Image(systemName: "info.circle.fill").foregroundStyle(Color.vsInfo)
        }
    }
}

struct CodeBadge: View {
    let diagnostic: Diagnostic

    var body: some View {
        let color: Color = diagnostic.isStyleCop ? .vsStyleCop : .severity(diagnostic.severity)
        Text(diagnostic.code)
            .font(.system(size: 10.5, weight: .semibold, design: .monospaced))
            .foregroundStyle(color)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(color.opacity(0.14), in: RoundedRectangle(cornerRadius: 4, style: .continuous))
    }
}

/// Build, test, engine and debugger output.
struct OutputPanel: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        LogView(lines: workspace.output.lines(in: workspace.output.selectedChannel))
    }
}

/// A fast read-only log (NSTextView) that appends new lines incrementally and highlights errors and warnings.
struct LogView: NSViewRepresentable {
    let lines: [OutputLog.Line]

    final class Coordinator {
        var shownIDs: [Int] = []
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSTextView.scrollableTextView()
        let text = scroll.documentView as! NSTextView
        text.isEditable = false
        text.isSelectable = true
        text.drawsBackground = true
        text.backgroundColor = Palette.editorBackground
        text.textContainerInset = NSSize(width: 8, height: 6)
        text.font = .monospacedSystemFont(ofSize: 11.5, weight: .regular)
        scroll.drawsBackground = false
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let text = scroll.documentView as? NSTextView, let storage = text.textStorage else { return }
        let ids = lines.map(\.id)
        let coordinator = context.coordinator
        let isAppend = coordinator.shownIDs.count <= ids.count && Array(ids.prefix(coordinator.shownIDs.count)) == coordinator.shownIDs
        let wasAtBottom = scroll.contentView.bounds.maxY >= text.bounds.maxY - 30
        let newLines = isAppend ? Array(lines.dropFirst(coordinator.shownIDs.count)) : lines
        if !isAppend {
            storage.setAttributedString(NSAttributedString())
        }
        guard !newLines.isEmpty else {
            coordinator.shownIDs = ids
            return
        }
        let font = NSFont.monospacedSystemFont(ofSize: 11.5, weight: .regular)
        let chunk = NSMutableAttributedString()
        for line in newLines {
            let lower = line.text.lowercased()
            let color: NSColor = line.isError || lower.contains(": error ") || lower.contains("error:") ? Palette.error
                : lower.contains(": warning ") ? Palette.warning
                : lower.contains("succeeded") || lower.contains("passed!") || lower.contains("bestanden") ? Palette.success
                : SyntaxColors.text
            chunk.append(NSAttributedString(string: line.text + "\n", attributes: [.font: font, .foregroundColor: color]))
        }
        storage.append(chunk)
        coordinator.shownIDs = ids
        if wasAtBottom || !isAppend {
            text.scrollToEndOfDocument(nil)
        }
    }
}
