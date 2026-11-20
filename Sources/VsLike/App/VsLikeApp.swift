import AppKit
import SwiftUI
import UniformTypeIdentifiers
import VsLikeKit

@main
struct VsLikeApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var workspace = AppDelegate.workspace

    var body: some Scene {
        Window("VsLike", id: "main") {
            MainView()
                .environment(workspace)
                .environment(workspace.preferences)
                .frame(minWidth: 900, minHeight: 560)
        }
        .windowToolbarStyle(.unified(showsTitle: false))
        .defaultSize(width: 1440, height: 900)
        .commands { AppCommands(workspace: workspace) }

        Settings {
            SettingsView()
                .environment(workspace.preferences)
                .environment(workspace)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    static let workspace = Workspace()
    private var pendingOpen: [URL] = []
    private var launched = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        // A plain `swift run` binary has no bundle; make it a regular app with a Dock icon.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        Preferences.shared.applyAppearance()
        launched = true
        let arguments = CommandLine.arguments.dropFirst().filter { !$0.hasPrefix("-") }
        let urls = pendingOpen + arguments.map { URL(fileURLWithPath: $0) }
        pendingOpen = []
        Self.open(urls)
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        guard launched else {
            pendingOpen += urls
            return
        }
        Self.open(urls)
    }

    /// Opens a solution, project or folder first (if any), then the remaining files as tabs.
    static func open(_ urls: [URL]) {
        let isRoot: (URL) -> Bool = { ["sln", "slnx", "csproj"].contains($0.pathExtension.lowercased()) || $0.hasDirectoryPath }
        let root = urls.first(where: isRoot) ?? urls.first
        guard let root else { return }
        Task {
            if workspace.rootURL == nil || isRoot(root) {
                await workspace.open(root)
            }
            for file in urls where file != root || !isRoot(root) {
                workspace.openFile(file)
            }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        let dirty = Self.workspace.openDocuments.filter(\.isDirty)
        if !dirty.isEmpty {
            switch Self.workspace.confirmSave(dirty.map(\.name)) {
            case .save: Self.workspace.saveAll()
            case .discard: break
            case .cancel: return .terminateCancel
            }
        }
        Self.workspace.terminals.stopRuns()
        Task {
            await Self.workspace.debugger.stop()
            await Self.workspace.engine?.stop()
        }
        return .terminateNow
    }
}

/// Open panels for solutions, projects, folders and files.
@MainActor
enum OpenPanel {
    static func chooseAndOpen(_ workspace: Workspace) {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = true
        panel.allowsMultipleSelection = false
        panel.message = String(localized: "Choose a solution, project, folder or C# file")
        panel.allowedContentTypes = ["sln", "slnx", "csproj", "cs"].compactMap { UTType(filenameExtension: $0) } + [.folder, .plainText]
        if panel.runModal() == .OK, let url = panel.url {
            Task { await workspace.open(url) }
        }
    }

    static func chooseFolder(message: String) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.message = message
        return panel.runModal() == .OK ? panel.url : nil
    }
}

struct AppCommands: Commands {
    let workspace: Workspace

    private var editor: CodeTextView? { workspace.activeEditor }
    private var diagram: DiagramEditorModel? {
        if case let .diagram(model) = workspace.activeTab { return model }
        return nil
    }

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("New Solution…") { workspace.isNewSolutionPresented = true }
                .keyboardShortcut("n", modifiers: [.command, .shift])
            Button("New File…") { workspace.promptNewFile(in: nil) }
                .keyboardShortcut("n")
                .disabled(!workspace.isOpen)
            Button("Add Project…") { workspace.newProjectTarget = workspace.solution }
                .disabled(workspace.solution == nil)
            Divider()
            Button("Open…") { OpenPanel.chooseAndOpen(workspace) }
                .keyboardShortcut("o")
            Menu("Open Recent") {
                ForEach(workspace.preferences.recentSolutions, id: \.self) { path in
                    Button(URL(fileURLWithPath: path).lastPathComponent) { Task { await workspace.open(URL(fileURLWithPath: path)) } }
                }
            }
        }
        CommandGroup(replacing: .saveItem) {
            Button("Save") { Task { await workspace.saveActive() } }
                .keyboardShortcut("s")
                .disabled(workspace.activeDocument == nil)
            Button("Save All") { workspace.saveAll() }
                .keyboardShortcut("s", modifiers: [.command, .option])
            Divider()
            Button("Close Tab") {
                if let tab = workspace.activeTab { workspace.close(tab) } else { NSApp.keyWindow?.performClose(nil) }
            }
            .keyboardShortcut("w")
            Button("Close Solution") { Task { _ = await workspace.closeAll() } }
                .disabled(!workspace.isOpen)
        }
        CommandGroup(replacing: .undoRedo) {
            Button("Undo") {
                if let diagram { Task { await diagram.undo() } } else { NSApp.sendAction(Selector(("undo:")), to: nil, from: nil) }
            }
            .keyboardShortcut("z")
            Button("Redo") {
                if let diagram { Task { await diagram.redo() } } else { NSApp.sendAction(Selector(("redo:")), to: nil, from: nil) }
            }
            .keyboardShortcut("z", modifiers: [.command, .shift])
        }
        CommandMenu("Code") {
            Button("Quick Actions and Fixes") { editor?.showQuickActions() }
                .keyboardShortcut(".", modifiers: [.command, .shift])
                .disabled(editor == nil)
            Button("Fix All StyleCop Issues in File") {
                if let document = workspace.activeDocument { Task { await workspace.fixAllStyleCop(in: document) } }
            }
            .keyboardShortcut(".", modifiers: [.command, .option, .shift])
            .disabled(workspace.activeDocument == nil || !workspace.styleCopEnabled)
            Divider()
            Button("Show Completions") { editor?.triggerCompletion() }
                .keyboardShortcut(.space, modifiers: .control)
            Button("Parameter Info") { editor?.showSignatureHelp() }
                .keyboardShortcut(.space, modifiers: [.command, .shift])
            Button("Rename Symbol…") { editor?.renameSymbol() }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF2FunctionKey)!)), modifiers: [])
            Button("Format Document") {
                if let document = workspace.activeDocument { Task { await workspace.formatDocument(document) } }
            }
            .keyboardShortcut("f", modifiers: [.option, .shift])
            Button("Toggle Comment") { editor?.toggleComment() }
                .keyboardShortcut("/")
            Divider()
            Toggle("StyleCop Analysis", isOn: Binding(get: { workspace.styleCopEnabled }, set: { _ in Task { await workspace.toggleStyleCop() } }))
                .disabled(workspace.solution == nil)
        }
        CommandMenu("Navigate") {
            Button("Quick Open…") { workspace.isQuickOpenPresented = true }
                .keyboardShortcut("p")
            Button("Command Palette…") { workspace.isCommandPalettePresented = true }
                .keyboardShortcut("p", modifiers: [.command, .shift])
            Divider()
            Button("Go to Definition") { editor?.goToDefinition() }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF12FunctionKey)!)), modifiers: [])
            Button("Find References") { editor?.findReferences() }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF12FunctionKey)!)), modifiers: .shift)
            Button("Go to Line…") { editor?.promptGoToLine() }
                .keyboardShortcut("l")
            Button("Find in Files") {
                workspace.sidebarTab = .search
                NSApp.sendAction(#selector(NSSplitViewController.toggleSidebar(_:)), to: nil, from: nil)
            }
            .keyboardShortcut("f", modifiers: [.command, .shift])
        }
        CommandMenu("Build") {
            Button("Build Solution") { workspace.build.start(.build) }
                .keyboardShortcut("b")
            Button("Rebuild Solution") { workspace.build.start(.rebuild) }
                .keyboardShortcut("b", modifiers: [.command, .shift])
            Button("Clean Solution") { workspace.build.start(.clean) }
                .keyboardShortcut("k", modifiers: [.command, .shift])
            Divider()
            Button("Run Without Debugging") { workspace.build.startWithoutDebugging() }
                .keyboardShortcut("r", modifiers: [.command, .control])
            Button("Run Tests") { workspace.tests.run() }
                .keyboardShortcut("u")
            Divider()
            Picker("Configuration", selection: Binding(get: { workspace.build.configuration }, set: { workspace.build.configuration = $0 })) {
                ForEach(BuildController.configurations, id: \.self) { Text($0).tag($0) }
            }
        }
        CommandMenu("Debug") {
            Button(workspace.debugger.isStopped ? "Continue" : "Start Debugging") { Task { await workspace.debugger.start() } }
                .keyboardShortcut("r")
            Button("Stop") {
                Task { await workspace.debugger.stop() }
                workspace.terminals.stopRuns()
                workspace.build.cancel()
            }
            .keyboardShortcut(".")
            Button("Pause") { Task { await workspace.debugger.pause() } }
                .disabled(!workspace.debugger.isActive || workspace.debugger.isStopped)
            Divider()
            Button("Step Over") { Task { await workspace.debugger.stepOver() } }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF10FunctionKey)!)), modifiers: [])
                .disabled(!workspace.debugger.isStopped)
            Button("Step Into") { Task { await workspace.debugger.stepIn() } }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF11FunctionKey)!)), modifiers: [])
                .disabled(!workspace.debugger.isStopped)
            Button("Step Out") { Task { await workspace.debugger.stepOut() } }
                .keyboardShortcut(KeyEquivalent(Character(UnicodeScalar(NSF11FunctionKey)!)), modifiers: .shift)
                .disabled(!workspace.debugger.isStopped)
            Divider()
            Button("Toggle Breakpoint") { editor?.toggleBreakpointAtCaret() }
                .keyboardShortcut("\\")
        }
        CommandGroup(after: .sidebar) {
            Button(workspace.isBottomPanelVisible ? "Hide Panel" : "Show Panel") { workspace.isBottomPanelVisible.toggle() }
                .keyboardShortcut("j")
            Button("Problems") { workspace.show(.problems) }
                .keyboardShortcut("m", modifiers: [.command, .shift])
            Button("Terminal") { workspace.showTerminal() }
                .keyboardShortcut("`", modifiers: .control)
            Button("Output") { workspace.show(.output) }
                .keyboardShortcut("o", modifiers: [.command, .shift])
            Button("Class Diagram") { Task { await workspace.openSolutionDiagram() } }
                .keyboardShortcut("d", modifiers: [.command, .shift])
                .disabled(workspace.solution == nil)
            Divider()
            Button("Larger Text") { workspace.preferences.fontSize = min(28, workspace.preferences.fontSize + 1) }
                .keyboardShortcut("+")
            Button("Smaller Text") { workspace.preferences.fontSize = max(9, workspace.preferences.fontSize - 1) }
                .keyboardShortcut("-")
            Button("Actual Size") { workspace.preferences.fontSize = 13 }
                .keyboardShortcut("0")
            Divider()
        }
    }
}
