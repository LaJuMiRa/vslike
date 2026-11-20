import SwiftUI
import VsLikeKit

/// The sidebar: a switcher for solution, files, search and tests above the matching list.
struct SidebarView: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        @Bindable var workspace = workspace
        VStack(spacing: 0) {
            HStack(spacing: 2) {
                ForEach(SidebarTab.allCases) { tab in
                    SidebarTabButton(tab: tab, isSelected: workspace.sidebarTab == tab) {
                        withAnimation(.snappy(duration: 0.2)) { workspace.sidebarTab = tab }
                    }
                }
            }
            .padding(4)
            .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .padding(.horizontal, 10)
            .padding(.top, 6)
            .padding(.bottom, 8)

            switch workspace.sidebarTab {
            case .solution:
                FileTreeList(nodes: workspace.tree, emptyText: "No solution loaded")
            case .files:
                FileTreeList(nodes: workspace.folderTree, emptyText: "Empty folder")
            case .search:
                SearchSidebar(search: workspace.search)
            case .tests:
                TestExplorer(tests: workspace.tests)
            }
        }
        .safeAreaInset(edge: .bottom) {
            EngineFooter()
        }
    }
}

private struct SidebarTabButton: View {
    let tab: SidebarTab
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: tab.symbol)
                .font(.system(size: 13, weight: isSelected ? .semibold : .regular))
                .frame(maxWidth: .infinity)
                .frame(height: 24)
                .foregroundStyle(isSelected ? Color.vsAccent : .secondary)
                .background {
                    if isSelected {
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .fill(.background)
                            .shadow(color: .black.opacity(0.1), radius: 1, y: 0.5)
                    }
                }
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(tab.title)
    }
}

/// State of the analysis engine at the bottom of the sidebar.
private struct EngineFooter: View {
    @Environment(Workspace.self) private var workspace

    var body: some View {
        HStack(spacing: 6) {
            switch workspace.engineStatus {
            case .idle:
                Circle().fill(.secondary).frame(width: 7, height: 7)
                Text("Analysis idle")
            case .starting, .loading:
                ProgressView().controlSize(.mini)
                Text("Loading solution…")
            case .ready:
                Circle().fill(Color.vsSuccess).frame(width: 7, height: 7)
                Text("IntelliSense ready")
                if workspace.styleCopEnabled {
                    Text("· StyleCop").foregroundStyle(Color.vsStyleCop)
                }
            case let .failed(message):
                Circle().fill(Color.vsError).frame(width: 7, height: 7)
                Text("Analysis unavailable").help(message)
                Button("Retry") { Task { await workspace.restartEngine() } }
                    .buttonStyle(.link)
            }
            Spacer()
        }
        .font(.system(size: 11))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
    }
}

/// The solution or folder tree with context menus.
struct FileTreeList: View {
    @Environment(Workspace.self) private var workspace
    let nodes: [FileNode]
    let emptyText: LocalizedStringKey

    var body: some View {
        @Bindable var workspace = workspace
        if nodes.isEmpty {
            ContentUnavailableView(emptyText, systemImage: "folder")
                .frame(maxHeight: .infinity)
        } else {
            List(selection: $workspace.selectedNodeID) {
                OutlineGroup(nodes, children: \.children) { node in
                    FileRow(node: node)
                        .tag(node.id)
                        .contextMenu { FileContextMenu(node: node) }
                }
            }
            .listStyle(.sidebar)
            .onChange(of: workspace.selectedNodeID) { _, id in
                guard let id, let node = find(id, in: nodes), case .file = node.kind, let url = node.url else { return }
                workspace.openFile(url)
            }
        }
    }

    private func find(_ id: String, in nodes: [FileNode]) -> FileNode? {
        for node in nodes {
            if node.id == id { return node }
            if let found = find(id, in: node.children ?? []) { return found }
        }
        return nil
    }
}

private struct FileRow: View {
    @Environment(Workspace.self) private var workspace
    let node: FileNode

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: node.symbol)
                .foregroundStyle(tint)
                .frame(width: 16)
            Text(node.name)
                .lineLimit(1)
                .fontWeight(isProject ? .medium : .regular)
            Spacer(minLength: 4)
            if let url = node.url, case .file = node.kind {
                let problems = workspace.diagnostics.diagnostics(for: url.path)
                let errors = problems.filter { $0.severity == .error }.count
                let warnings = problems.filter { $0.severity == .warning }.count
                if errors > 0 {
                    Text("\(errors)").font(.caption2.monospacedDigit().weight(.semibold)).foregroundStyle(Color.vsError)
                } else if warnings > 0 {
                    Text("\(warnings)").font(.caption2.monospacedDigit()).foregroundStyle(Color.vsWarning)
                }
                if workspace.openDocuments.contains(where: { $0.path == url.path && $0.isDirty }) {
                    Circle().fill(.secondary).frame(width: 6, height: 6)
                }
            }
            if case let .project(isTest) = node.kind, let url = node.url, url.path == workspace.build.startupProject, !isTest {
                Image(systemName: "play.circle.fill").font(.caption).foregroundStyle(Color.vsAccent).help("Startup project")
            }
        }
    }

    private var isProject: Bool {
        if case .project = node.kind { return true }
        return false
    }

    private var tint: Color {
        switch node.kind {
        case .project: return .vsAccent
        case .folder, .solutionFolder: return Color(nsColor: .systemTeal).opacity(0.85)
        case .dependencies, .reference, .package: return .secondary
        case .file:
            if node.name.hasSuffix(".cs") { return Color(nsColor: SyntaxColors.type) }
            if node.name.hasSuffix(DiagramDocument.fileExtension) { return .vsAccent }
            return .secondary
        }
    }
}

private struct FileContextMenu: View {
    @Environment(Workspace.self) private var workspace
    let node: FileNode

    var body: some View {
        switch node.kind {
        case .project:
            if let url = node.url {
                Button("New File…", systemImage: "doc.badge.plus") { workspace.promptNewFile(in: url.deletingLastPathComponent()) }
                Button("New Folder…", systemImage: "folder.badge.plus") { workspace.promptNewFolder(in: url.deletingLastPathComponent()) }
                Divider()
                Button("Class Diagram", systemImage: "rectangle.3.group") { Task { await workspace.openDiagram(forFolder: url.deletingLastPathComponent()) } }
                Button("Manage NuGet Packages…", systemImage: "cube.box") { workspace.nugetProject = url.path }
                Button("Properties…", systemImage: "slider.horizontal.3") { workspace.propertiesProject = url.path }
                Button("Set as Startup Project", systemImage: "play.circle") { workspace.build.startupProject = url.path }
                Divider()
                Button("Open Project File", systemImage: "doc.text") { workspace.openFile(url) }
                Button("Reveal in Finder", systemImage: "folder") { workspace.revealInFinder(url) }
                Divider()
                Button("Remove from Solution", systemImage: "minus.circle", role: .destructive) { Task { await workspace.removeProjectFromSolution(url.path) } }
            }
        case .folder:
            if let url = node.url {
                Button("New File…", systemImage: "doc.badge.plus") { workspace.promptNewFile(in: url) }
                Button("New Folder…", systemImage: "folder.badge.plus") { workspace.promptNewFolder(in: url) }
                Button("Class Diagram", systemImage: "rectangle.3.group") { Task { await workspace.openDiagram(forFolder: url) } }
                Divider()
                Button("Rename…", systemImage: "pencil") { workspace.promptRename(url) }
                Button("Reveal in Finder", systemImage: "folder") { workspace.revealInFinder(url) }
                Button("Open in Terminal", systemImage: "terminal") { workspace.showTerminal() }
                Divider()
                Button("Move to Trash", systemImage: "trash", role: .destructive) { Task { await workspace.moveToTrash(url) } }
            }
        case .file:
            if let url = node.url {
                Button("Open", systemImage: "doc") { workspace.openFile(url) }
                Button("Rename…", systemImage: "pencil") { workspace.promptRename(url) }
                Button("Reveal in Finder", systemImage: "folder") { workspace.revealInFinder(url) }
                Button("Copy Path", systemImage: "doc.on.doc") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(url.path, forType: .string)
                }
                Divider()
                Button("Move to Trash", systemImage: "trash", role: .destructive) { Task { await workspace.moveToTrash(url) } }
            }
        case .dependencies:
            if let project = projectPath {
                Button("Add Project Reference…", systemImage: "arrow.triangle.branch") { workspace.propertiesProject = project }
                Button("Manage NuGet Packages…", systemImage: "cube.box") { workspace.nugetProject = project }
            }
        case let .reference(path):
            if let project = projectPath {
                Button("Remove Reference", systemImage: "minus.circle", role: .destructive) { Task { await workspace.removeProjectReference(path, from: project) } }
            }
        case .package:
            if let project = projectPath {
                Button("Manage NuGet Packages…", systemImage: "cube.box") { workspace.nugetProject = project }
            }
        case .solutionFolder:
            Button("Add Project…", systemImage: "plus") { workspace.newProjectTarget = workspace.solution }
        }
    }

    /// The project file of a dependency node (ids are "deps:<project>", "ref:<project>>…", "pkg:<project>>…").
    private var projectPath: String? {
        let id = node.id
        if id.hasPrefix("deps:") { return String(id.dropFirst(5)) }
        if let range = id.range(of: ">"), let colon = id.firstIndex(of: ":") {
            return String(id[id.index(after: colon)..<range.lowerBound])
        }
        return nil
    }
}
