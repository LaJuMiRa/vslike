import AppKit
import Observation
import VsLikeKit

/// A tab in the editor area.
@MainActor
enum EditorTab: @MainActor Identifiable, @MainActor Equatable {
    case code(CodeDocument)
    case diagram(DiagramEditorModel)

    var id: UUID {
        switch self {
        case let .code(document): return document.id
        case let .diagram(diagram): return diagram.id
        }
    }

    var title: String {
        switch self {
        case let .code(document): return document.name
        case let .diagram(diagram): return diagram.name
        }
    }

    var symbol: String {
        switch self {
        case let .code(document): return FileNode.symbol(forFile: document.name)
        case .diagram: return "rectangle.3.group"
        }
    }

    var isDirty: Bool {
        if case let .code(document) = self { return document.isDirty }
        return false
    }

    static func == (lhs: EditorTab, rhs: EditorTab) -> Bool { lhs.id == rhs.id }
}

enum SidebarTab: String, CaseIterable, Identifiable {
    case solution, files, search, tests
    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .solution: return "square.stack.3d.up"
        case .files: return "folder"
        case .search: return "magnifyingglass"
        case .tests: return "checkmark.diamond"
        }
    }

    var title: String {
        switch self {
        case .solution: return String(localized: "Solution")
        case .files: return String(localized: "Files")
        case .search: return String(localized: "Search")
        case .tests: return String(localized: "Tests")
        }
    }
}

enum BottomTab: String, CaseIterable, Identifiable {
    case problems, output, terminal, debug
    var id: String { rawValue }
}

enum EngineStatus: Equatable {
    case idle, starting, loading, ready, failed(String)
}

/// A transient message for the status pill in the toolbar.
struct StatusMessage: Equatable {
    enum Style: Equatable { case neutral, progress, success, warning, error }
    var text: String
    var style: Style = .neutral
}

/// Everything about the open solution: tree, documents, engine, diagnostics and the build, test, debug and terminal
/// controllers. One per window.
@MainActor
@Observable
final class Workspace {
    let preferences: Preferences
    let diagnostics = DiagnosticsStore()
    let output = OutputLog()
    @ObservationIgnored private(set) lazy var build = BuildController(workspace: self)
    @ObservationIgnored private(set) lazy var tests = TestController(workspace: self)
    @ObservationIgnored private(set) lazy var debugger = DebugController(workspace: self)
    @ObservationIgnored private(set) lazy var terminals = TerminalController(workspace: self)
    @ObservationIgnored private(set) lazy var search = SearchModel(workspace: self)

    private(set) var rootURL: URL?
    private(set) var solutionURL: URL?
    private(set) var solution: SolutionModel?
    private(set) var tree: [FileNode] = []
    private(set) var folderTree: [FileNode] = []
    private(set) var projects: [WorkspaceProject] = []
    private(set) var engineStatus: EngineStatus = .idle
    var settings = SolutionSettings()

    var tabs: [EditorTab] = []
    var activeTabID: UUID?
    var sidebarTab: SidebarTab = .solution
    var bottomTab: BottomTab = .problems
    var isBottomPanelVisible = true
    var isInspectorVisible = false
    var status = StatusMessage(text: String(localized: "Ready"))
    var styleCopEnabled = false
    var isStyleCopLoading = false
    var selectedNodeID: String?

    // Sheets and overlays.
    var isCommandPalettePresented = false
    var isQuickOpenPresented = false
    var isNewSolutionPresented = false
    var newProjectTarget: SolutionModel?
    var nugetProject: String?
    var propertiesProject: String?
    var prompt: PromptRequest?
    var alert: AlertMessage?
    var references: ReferenceList?

    @ObservationIgnored private(set) var engine: EngineClient?
    @ObservationIgnored private var notificationTask: Task<Void, Never>?
    @ObservationIgnored private var syncTasks: [UUID: Task<Void, Never>] = [:]
    @ObservationIgnored private var watcher: FolderWatcher?
    @ObservationIgnored private var treeRefresh: Task<Void, Never>?
    @ObservationIgnored var documentListeners: [(CodeDocument) -> Void] = []
    /// The editor that has (or last had) keyboard focus, for menu commands.
    @ObservationIgnored weak var activeEditor: CodeTextView?

    init(preferences: Preferences = .shared) {
        self.preferences = preferences
    }

    var isOpen: Bool { rootURL != nil }

    var title: String { solution?.name ?? rootURL?.lastPathComponent ?? "VsLike" }

    var activeTab: EditorTab? { tabs.first { $0.id == activeTabID } }

    var activeDocument: CodeDocument? {
        if case let .code(document) = activeTab { return document }
        return nil
    }

    var openDocuments: [CodeDocument] {
        tabs.compactMap { if case let .code(document) = $0 { return document } else { return nil } }
    }

    var isEngineReady: Bool { engineStatus == .ready }

    // MARK: Opening

    /// Opens a solution, project, folder or single file. A folder with exactly one solution opens that solution.
    func open(_ url: URL) async {
        let url = RealPath.resolve(url)
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory) else {
            alert = AlertMessage(title: String(localized: "Not found"), message: url.path)
            return
        }
        if !isDirectory.boolValue && url.pathExtension.lowercased() == "cs" && rootURL == nil {
            await open(url.deletingLastPathComponent())
            openFile(url)
            return
        }
        if !isDirectory.boolValue && !["sln", "slnx", "csproj"].contains(url.pathExtension.lowercased()) {
            if rootURL == nil {
                await open(url.deletingLastPathComponent())
            }
            openFile(url)
            return
        }
        guard await closeAll() else {
            return
        }
        var target = url
        if isDirectory.boolValue {
            let entries = (try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []
            let solutions = entries.filter { $0.hasSuffix(".slnx") || $0.hasSuffix(".sln") }.sorted()
            if solutions.count == 1 {
                target = url.appendingPathComponent(solutions[0])
            } else if let csproj = entries.first(where: { $0.hasSuffix(".csproj") }), solutions.isEmpty {
                target = url.appendingPathComponent(csproj)
            }
        }
        let root = target.hasDirectoryPath || isDirectory.boolValue && target == url ? url : target.deletingLastPathComponent()
        rootURL = root
        solutionURL = target == url && isDirectory.boolValue ? nil : target
        if let solutionURL, ["sln", "slnx"].contains(solutionURL.pathExtension.lowercased()) {
            solution = try? SolutionReader.read(solutionURL)
        } else if let solutionURL {
            let project = SolutionProject(name: solutionURL.deletingPathExtension().lastPathComponent, filePath: solutionURL.path, solutionFolder: "/")
            solution = SolutionModel(filePath: solutionURL.path, format: .slnx, projects: [project], folders: [])
        }
        settings = solutionURL.map(SolutionSettings.load(for:)) ?? SolutionSettings()
        styleCopEnabled = settings.styleCopEnabled ?? preferences.styleCopByDefault
        preferences.noteRecent((solutionURL ?? root).path)
        NSDocumentController.shared.noteNewRecentDocumentURL(solutionURL ?? root)
        sidebarTab = solution == nil ? .files : .solution
        refreshTree()
        watcher = FolderWatcher(url: root) { [weak self] paths in self?.filesChanged(paths) }
        build.restoreSelection()
        if let solutionURL {
            await startEngine(loading: solutionURL)
        }
        tests.reloadProjects()
    }

    /// Closes the solution after asking about unsaved changes. Returns false when the user cancelled.
    func closeAll() async -> Bool {
        let dirty = openDocuments.filter(\.isDirty)
        if !dirty.isEmpty {
            switch confirmSave(dirty.map(\.name)) {
            case .save:
                for document in dirty { save(document) }
            case .discard:
                break
            case .cancel:
                return false
            }
        }
        await debugger.stop()
        build.cancel()
        tabs.removeAll()
        activeTabID = nil
        diagnostics.clear()
        watcher?.stop()
        watcher = nil
        notificationTask?.cancel()
        if let engine {
            await engine.stop()
        }
        engine = nil
        engineStatus = .idle
        rootURL = nil
        solutionURL = nil
        solution = nil
        projects = []
        tree = []
        folderTree = []
        return true
    }

    enum SaveChoice { case save, discard, cancel }

    func confirmSave(_ names: [String]) -> SaveChoice {
        let alert = NSAlert()
        alert.messageText = names.count == 1
            ? String(localized: "Save changes to \(names[0])?")
            : String(localized: "Save changes to \(names.count) files?")
        alert.informativeText = String(localized: "Your changes will be lost if you don't save them.")
        alert.addButton(withTitle: String(localized: "Save"))
        alert.addButton(withTitle: String(localized: "Cancel"))
        alert.addButton(withTitle: String(localized: "Don't Save"))
        switch alert.runModal() {
        case .alertFirstButtonReturn: return .save
        case .alertThirdButtonReturn: return .discard
        default: return .cancel
        }
    }

    // MARK: Tree and file system

    func refreshTree() {
        guard let rootURL else {
            return
        }
        if let solutionURL, let reread = try? SolutionReader.read(solutionURL), ["sln", "slnx"].contains(solutionURL.pathExtension.lowercased()) {
            solution = reread
        }
        folderTree = FileTreeBuilder.files(in: rootURL, depth: 0)
        tree = solution.map(FileTreeBuilder.solutionTree) ?? folderTree
    }

    private func filesChanged(_ paths: [String]) {
        let relevant = paths.filter { path in
            !path.split(separator: "/").contains { FileTreeBuilder.ignored.contains(String($0)) }
        }
        guard !relevant.isEmpty else {
            return
        }
        treeRefresh?.cancel()
        treeRefresh = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(250))
            guard !Task.isCancelled, let self else { return }
            self.refreshTree()
            for path in Set(relevant) {
                self.syncFromDisk(path)
            }
            if relevant.contains(where: { $0.hasSuffix(".csproj") || $0.hasSuffix(".slnx") || $0.hasSuffix(".sln") }) {
                self.tests.reloadProjects()
            }
        }
    }

    /// Keeps open documents and the engine in step with external changes.
    private func syncFromDisk(_ path: String) {
        let exists = FileManager.default.fileExists(atPath: path)
        if let document = openDocuments.first(where: { $0.path == path }) {
            if exists, !document.isDirty, let disk = try? String(contentsOfFile: path, encoding: .utf8) {
                document.reload(from: disk.replacingOccurrences(of: "\r\n", with: "\n"))
            }
            return
        }
        guard path.hasSuffix(".cs"), let engine, isEngineReady else {
            return
        }
        if exists, let text = try? String(contentsOfFile: path, encoding: .utf8) {
            Task {
                if (try? await engine.addDocument(path, text: text)) != true {
                    engine.updateDocument(path, text: text)
                }
                self.notifyCodeChanged()
            }
        } else if !exists {
            Task {
                _ = try? await engine.removeDocument(path)
                self.notifyCodeChanged()
            }
        }
    }

    /// Informs open diagrams that the code changed.
    func notifyCodeChanged() {
        for case let .diagram(diagram) in tabs {
            diagram.scheduleRefresh()
        }
    }

    // MARK: Engine

    static func engineDLL() -> URL {
        if let override = ProcessInfo.processInfo.environment["VSLIKE_ENGINE_DLL"] {
            return URL(fileURLWithPath: override)
        }
        if let bundled = Bundle.main.resourceURL?.appendingPathComponent("engine/VsLikeEngine.dll"), FileManager.default.fileExists(atPath: bundled.path) {
            return bundled
        }
        // Development: the engine built from this repository.
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        return repository.appendingPathComponent("engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll")
    }

    private func startEngine(loading solutionURL: URL) async {
        engineStatus = .starting
        status = StatusMessage(text: String(localized: "Starting analysis…"), style: .progress)
        do {
            let runner = try CommandRunner.dotnet(language: preferences.effectiveLanguage)
            let engine = EngineClient(engineDLL: Self.engineDLL(), runner: runner)
            try engine.start { [weak self] code in
                Task { @MainActor in self?.engineExited(code: code, client: engine) }
            }
            self.engine = engine
            listen(to: engine)
            let info = try await engine.initialize(culture: preferences.effectiveLanguage)
            output.append("Analysis engine \(info.engineVersion), .NET SDK \(info.sdkVersion)", channel: .engine)
            engineStatus = .loading
            status = StatusMessage(text: String(localized: "Loading \(solutionURL.lastPathComponent)…"), style: .progress)
            let result = try await engine.load(solutionURL.path)
            projects = result.projects
            for warning in result.warnings {
                output.append(warning, channel: .engine, isError: true)
            }
            for document in openDocuments where document.isCSharp {
                engine.updateDocument(document.path, text: document.text)
            }
            engineStatus = .ready
            status = StatusMessage(text: String(localized: "\(result.projects.count) projects loaded"), style: .success)
            if styleCopEnabled {
                await applyStyleCop(true, persist: false)
            }
            try? await engine.refreshAllDiagnostics()
            notifyCodeChanged()
        } catch {
            engineStatus = .failed(error.localizedDescription)
            status = StatusMessage(text: String(localized: "Analysis unavailable"), style: .error)
            output.append(error.localizedDescription, channel: .engine, isError: true)
            if let log = engine?.recentLog, !log.isEmpty {
                output.append(log, channel: .engine, isError: true)
            }
        }
    }

    private func engineExited(code: Int32, client: EngineClient) {
        guard client === engine, engineStatus != .idle else {
            return
        }
        engineStatus = .failed(String(localized: "The analysis engine stopped (exit \(code))."))
        status = StatusMessage(text: String(localized: "Analysis stopped"), style: .error)
        output.append(client.recentLog, channel: .engine, isError: true)
    }

    func restartEngine() async {
        guard let solutionURL else { return }
        if let engine { await engine.stop() }
        engine = nil
        await startEngine(loading: solutionURL)
    }

    private func listen(to engine: EngineClient) {
        notificationTask?.cancel()
        notificationTask = Task { [weak self] in
            for await notification in engine.notifications {
                guard let self else { return }
                if notification.method == "workspace/diagnostics",
                   let list = try? notification.params["diagnostics"]?.decode([Diagnostic].self) {
                    self.diagnostics.setLive(list, for: notification.params["path"]?.stringValue)
                }
            }
        }
    }

    /// Pushes a document's text to the engine now (before requests that need the latest text).
    func syncNow(_ document: CodeDocument) {
        syncTasks[document.id]?.cancel()
        syncTasks[document.id] = nil
        if document.isCSharp, isEngineReady {
            engine?.updateDocument(document.path, text: document.text)
        }
    }

    private func documentChanged(_ document: CodeDocument) {
        syncTasks[document.id]?.cancel()
        syncTasks[document.id] = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(120))
            guard !Task.isCancelled, let self else { return }
            self.syncTasks[document.id] = nil
            self.syncNow(document)
            self.notifyCodeChanged()
        }
        documentListeners.forEach { $0(document) }
    }

    // MARK: StyleCop

    func toggleStyleCop() async {
        await applyStyleCop(!styleCopEnabled, persist: true)
    }

    func applyStyleCop(_ enabled: Bool, persist: Bool) async {
        styleCopEnabled = enabled
        if persist, let solutionURL {
            settings.styleCopEnabled = enabled
            try? settings.save(for: solutionURL)
        }
        guard let engine, isEngineReady else {
            return
        }
        isStyleCopLoading = true
        defer { isStyleCopLoading = false }
        status = StatusMessage(text: enabled ? String(localized: "Loading StyleCop…") : String(localized: "StyleCop off"), style: enabled ? .progress : .neutral)
        do {
            let loaded = try await engine.setStyleCop(enabled)
            if enabled && !loaded {
                styleCopEnabled = false
                alert = AlertMessage(title: "StyleCop", message: String(localized: "StyleCop could not be downloaded. Check your internet connection and try again."))
                return
            }
            status = StatusMessage(text: enabled ? String(localized: "StyleCop on") : String(localized: "StyleCop off"), style: enabled ? .success : .neutral)
        } catch {
            alert = AlertMessage(title: "StyleCop", message: error.localizedDescription)
        }
    }

    // MARK: Documents and tabs

    @discardableResult
    func openFile(_ url: URL, line: Int? = nil, column: Int = 1, selectLength: Int = 0) -> CodeDocument? {
        if url.lastPathComponent.hasSuffix(DiagramDocument.fileExtension) {
            openDiagram(url)
            return nil
        }
        let path = RealPath.resolve(url.path)
        let document: CodeDocument
        if let existing = openDocuments.first(where: { $0.path == path }) {
            document = existing
        } else {
            do {
                document = try CodeDocument.load(URL(fileURLWithPath: path))
            } catch {
                alert = AlertMessage(title: String(localized: "Cannot open file"), message: error.localizedDescription)
                return nil
            }
            document.onChange = { [weak self] in self?.documentChanged($0) }
            document.breakpoints = Dictionary(uniqueKeysWithValues: (settings.breakpoints?[path] ?? []).map { ($0.line, $0) })
            insertTab(.code(document))
            if document.isCSharp, isEngineReady {
                engine?.updateDocument(path, text: document.text)
            }
        }
        if let line {
            let offset = document.offset(line: line, column: column)
            document.pendingSelection = NSRange(location: offset, length: selectLength)
        }
        activeTabID = document.id
        return document
    }

    func openDiagram(_ url: URL) {
        if let existing = tabs.first(where: { if case let .diagram(d) = $0 { return d.url == url } else { return false } }) {
            activeTabID = existing.id
            return
        }
        let diagram = DiagramEditorModel(url: url, workspace: self)
        insertTab(.diagram(diagram))
        activeTabID = diagram.id
        diagram.scheduleRefresh(immediately: true)
    }

    private func insertTab(_ tab: EditorTab) {
        if let active = tabs.firstIndex(where: { $0.id == activeTabID }) {
            tabs.insert(tab, at: active + 1)
        } else {
            tabs.append(tab)
        }
    }

    func close(_ tab: EditorTab) {
        if case let .code(document) = tab, document.isDirty {
            switch confirmSave([document.name]) {
            case .save: save(document)
            case .discard:
                if document.isCSharp, let disk = try? String(contentsOf: document.url, encoding: .utf8) {
                    engine?.updateDocument(document.path, text: disk)
                }
            case .cancel: return
            }
        }
        guard let index = tabs.firstIndex(of: tab) else {
            return
        }
        tabs.remove(at: index)
        if activeTabID == tab.id {
            activeTabID = tabs.isEmpty ? nil : tabs[min(index, tabs.count - 1)].id
        }
    }

    func closeOtherTabs(than tab: EditorTab) {
        for other in tabs where other != tab {
            close(other)
        }
    }

    func save(_ document: CodeDocument) {
        do {
            try document.save()
            status = StatusMessage(text: String(localized: "Saved \(document.name)"), style: .neutral)
        } catch {
            alert = AlertMessage(title: String(localized: "Saving failed"), message: error.localizedDescription)
        }
    }

    func saveAll() {
        for document in openDocuments where document.isDirty {
            save(document)
        }
    }

    func saveActive() async {
        guard let document = activeDocument else {
            return
        }
        if preferences.formatOnSave && document.isCSharp {
            await formatDocument(document)
        }
        save(document)
    }

    func persistBreakpoints() {
        guard let solutionURL else { return }
        var all = settings.breakpoints ?? [:]
        for document in openDocuments {
            let list = document.breakpoints.values.sorted { $0.line < $1.line }
            all[document.path] = list.isEmpty ? nil : list
        }
        settings.breakpoints = all.isEmpty ? nil : all
        try? settings.save(for: solutionURL)
    }

    /// All breakpoints of the solution, including files that are not open.
    var allBreakpoints: [String: [SourceBreakpoint]] {
        var all = settings.breakpoints ?? [:]
        for document in openDocuments {
            let list = document.breakpoints.values.sorted { $0.line < $1.line }
            all[document.path] = list.isEmpty ? nil : list
        }
        return all
    }

    /// The project whose folder contains a file (deepest match).
    func project(containing path: String) -> SolutionProject? {
        solution?.projects
            .filter { path.hasPrefix($0.directory.path + "/") }
            .max { $0.directory.path.count < $1.directory.path.count }
    }
}

struct AlertMessage: Identifiable {
    let id = UUID()
    let title: String
    let message: String
}

/// A text prompt (new file name, rename …) shown as a sheet.
struct PromptRequest: Identifiable {
    let id = UUID()
    let title: String
    let message: String
    let placeholder: String
    var initialValue: String = ""
    let confirmTitle: String
    let action: @MainActor (String) async -> Void
}

/// A list of references or definitions shown in a popover-like sheet.
struct ReferenceList: Identifiable {
    let id = UUID()
    let title: String
    let locations: [SourceLocation]
}
