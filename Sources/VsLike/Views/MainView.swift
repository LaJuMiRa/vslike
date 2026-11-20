import SwiftUI
import VsLikeKit

/// The main window: sidebar, tabs with editors or diagrams, the bottom panel and the toolbar.
struct MainView: View {
    @Environment(Workspace.self) private var workspace
    @State private var columnVisibility = NavigationSplitViewVisibility.all

    var body: some View {
        @Bindable var workspace = workspace
        Group {
            if workspace.isOpen {
                NavigationSplitView(columnVisibility: $columnVisibility) {
                    SidebarView()
                        .navigationSplitViewColumnWidth(min: 220, ideal: 280, max: 460)
                } detail: {
                    HStack(spacing: 0) {
                        DetailArea()
                        if workspace.isInspectorVisible {
                            Divider()
                            InspectorView()
                                .frame(width: 310)
                                .transition(.move(edge: .trailing))
                        }
                    }
                }
                .toolbar { WorkspaceToolbar() }
            } else {
                WelcomeView()
                    .toolbar { ToolbarItem { Color.clear.frame(width: 1) } }
            }
        }
        .navigationTitle(workspace.title)
        .navigationSubtitle(workspace.solution.map { "\($0.projects.count) " + String(localized: "projects") } ?? "")
        .tint(.vsAccent)
        .overlay {
            if workspace.isCommandPalettePresented || workspace.isQuickOpenPresented {
                CommandPalette(mode: workspace.isQuickOpenPresented ? .files : .commands)
            }
        }
        .sheet(isPresented: $workspace.isNewSolutionPresented) { NewSolutionSheet() }
        .sheet(item: $workspace.newProjectTarget) { solution in NewProjectSheet(solution: solution) }
        .sheet(item: Binding(get: { workspace.nugetProject.map(IdentifiedPath.init) }, set: { workspace.nugetProject = $0?.path })) { item in
            NuGetSheet(project: item.path)
        }
        .sheet(item: Binding(get: { workspace.propertiesProject.map(IdentifiedPath.init) }, set: { workspace.propertiesProject = $0?.path })) { item in
            ProjectPropertiesSheet(project: item.path)
        }
        .sheet(item: $workspace.prompt) { request in PromptSheet(request: request) }
        .sheet(item: $workspace.references) { list in ReferencesSheet(list: list) }
        .alert(item: $workspace.alert) { alert in
            Alert(title: Text(alert.title), message: Text(alert.message))
        }
        .onChange(of: workspace.tabs.map(\.id)) {
            EditorRegistry.shared.prune(keeping: workspace.openDocuments)
        }
    }
}

struct IdentifiedPath: Identifiable {
    let path: String
    var id: String { path }
}

/// Tabs, the active editor or diagram and the bottom panel in a resizable split.
private struct DetailArea: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        VSplitView {
            VStack(spacing: 0) {
                if !workspace.tabs.isEmpty {
                    TabBar()
                }
                Group {
                    switch workspace.activeTab {
                    case let .code(document):
                        CodeEditorPane(document: document)
                            .id(document.id)
                    case let .diagram(diagram):
                        DiagramEditorView(model: diagram)
                            .id(diagram.id)
                    case nil:
                        EmptyEditorView()
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .frame(minHeight: 200)
            .layoutPriority(1)
            if workspace.isBottomPanelVisible {
                BottomPanel()
                    .frame(minHeight: 120, idealHeight: 260)
            }
        }
    }
}

/// Shown when no tab is open: shortcuts to the most useful actions.
private struct EmptyEditorView: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        VStack(spacing: 22) {
            Image(systemName: "chevron.left.forwardslash.chevron.right")
                .font(.system(size: 44, weight: .light))
                .foregroundStyle(.tertiary)
            VStack(spacing: 10) {
                ShortcutRow(title: "Quick Open", keys: "⌘P")
                ShortcutRow(title: "Command Palette", keys: "⇧⌘P")
                ShortcutRow(title: "Quick Actions and Fixes", keys: "⇧⌘.")
                ShortcutRow(title: "Build", keys: "⌘B")
                ShortcutRow(title: "Run with Debugger", keys: "⌘R")
                ShortcutRow(title: "Class Diagram", keys: "⇧⌘D")
                ShortcutRow(title: "Terminal", keys: "⌃`")
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(nsColor: Palette.editorBackground))
    }
}

private struct ShortcutRow: View {
    let title: LocalizedStringKey
    let keys: String

    var body: some View {
        HStack {
            Text(title).foregroundStyle(.secondary)
            Spacer(minLength: 40)
            Text(keys)
                .font(.system(.body, design: .rounded).weight(.medium))
                .padding(.horizontal, 8)
                .padding(.vertical, 2)
                .background(.quaternary.opacity(0.6), in: RoundedRectangle(cornerRadius: 6))
        }
        .frame(width: 300)
    }
}

/// Slim tabs with icon, name, a dot for unsaved changes and a close button on hover.
struct TabBar: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 2) {
                ForEach(workspace.tabs) { tab in
                    TabButton(tab: tab, isActive: tab.id == workspace.activeTabID)
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
        }
        .frame(height: 36)
        .background(.bar)
        .overlay(alignment: .bottom) { Divider() }
    }
}

private struct TabButton: View {
    @Environment(Workspace.self) private var workspace
    let tab: EditorTab
    let isActive: Bool
    @State private var isHovering = false

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: tab.symbol)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(isActive ? Color.vsAccent : .secondary)
            Text(tab.title)
                .font(.system(size: 12, weight: isActive ? .medium : .regular))
                .lineLimit(1)
            ZStack {
                if tab.isDirty && !isHovering {
                    Circle().fill(Color.secondary).frame(width: 7, height: 7)
                } else {
                    Button {
                        workspace.close(tab)
                    } label: {
                        Image(systemName: "xmark").font(.system(size: 9, weight: .bold))
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
                    .opacity(isHovering || isActive ? 1 : 0)
                }
            }
            .frame(width: 14, height: 14)
        }
        .padding(.leading, 10)
        .padding(.trailing, 6)
        .frame(height: 26)
        .background {
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(isActive ? AnyShapeStyle(.background) : AnyShapeStyle(isHovering ? Color.primary.opacity(0.06) : .clear))
                .shadow(color: .black.opacity(isActive ? 0.12 : 0), radius: 1, y: 0.5)
        }
        .contentShape(Rectangle())
        .onTapGesture { workspace.activeTabID = tab.id }
        .onHover { isHovering = $0 }
        .contextMenu {
            Button("Close") { workspace.close(tab) }
            Button("Close Others") { workspace.closeOtherTabs(than: tab) }
            if case let .code(document) = tab {
                Divider()
                Button("Reveal in Finder") { workspace.revealInFinder(document.url) }
                Button("Copy Path") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(document.path, forType: .string)
                }
            }
        }
        .help(tabHelp)
    }

    private var tabHelp: String {
        switch tab {
        case let .code(document): return document.path
        case let .diagram(diagram): return diagram.url.path
        }
    }
}

/// Right-hand inspector: the selected diagram type, otherwise file information.
private struct InspectorView: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        switch workspace.activeTab {
        case let .diagram(model):
            DiagramInspector(model: model)
        case let .code(document):
            FileInspector(document: document)
        case nil:
            ContentUnavailableView("No Selection", systemImage: "sidebar.right")
        }
    }
}

private struct FileInspector: View {
    @Environment(Workspace.self) private var workspace
    let document: CodeDocument

    var body: some View {
        Form {
            Section("File") {
                LabeledContent("Name", value: document.name)
                LabeledContent("Language", value: document.languageName)
                LabeledContent("Lines", value: "\(document.position(of: document.storage.length).line)")
                if let project = workspace.project(containing: document.path) {
                    LabeledContent("Project", value: project.name)
                }
            }
            Section("Problems") {
                let problems = workspace.diagnostics.diagnostics(for: document.path)
                LabeledContent("Errors", value: "\(problems.filter { $0.severity == .error }.count)")
                LabeledContent("Warnings", value: "\(problems.filter { $0.severity == .warning && !$0.isStyleCop }.count)")
                LabeledContent("StyleCop", value: "\(problems.filter(\.isStyleCop).count)")
                if workspace.styleCopEnabled {
                    Button("Fix All StyleCop Issues") { Task { await workspace.fixAllStyleCop(in: document) } }
                }
            }
            Section("Breakpoints") {
                if document.breakpoints.isEmpty {
                    Text("Click a line number to add one.").foregroundStyle(.secondary)
                }
                ForEach(document.breakpoints.values.sorted { $0.line < $1.line }, id: \.line) { breakpoint in
                    HStack {
                        Circle().fill(Color(nsColor: Palette.breakpoint)).frame(width: 8, height: 8)
                        Text("Line \(breakpoint.line)")
                        if let condition = breakpoint.condition {
                            Text(condition).font(.caption.monospaced()).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}
