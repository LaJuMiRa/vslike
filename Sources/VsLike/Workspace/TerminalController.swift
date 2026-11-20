import AppKit
import Observation
import SwiftTerm
import VsLikeKit

/// One terminal tab: an interactive login shell or a program started by Run.
@MainActor
@Observable
final class TerminalSession: Identifiable {
    let id = UUID()
    var title: String
    let isRun: Bool
    private(set) var isRunning = true
    private(set) var exitCode: Int32?
    @ObservationIgnored let view: LocalProcessTerminalView
    @ObservationIgnored private var delegateBox: TerminalDelegate?

    init(title: String, isRun: Bool, font: NSFont) {
        self.title = title
        self.isRun = isRun
        view = LocalProcessTerminalView(frame: NSRect(x: 0, y: 0, width: 800, height: 300))
        view.font = font
        view.optionAsMetaKey = false
        applyColors()
        let delegate = TerminalDelegate(session: self)
        delegateBox = delegate
        view.processDelegate = delegate
    }

    func applyColors() {
        let dark = NSApp.effectiveAppearance.isDark
        view.nativeBackgroundColor = dark ? NSColor(hex: 0x15171B) : NSColor(hex: 0xFBFBFC)
        view.nativeForegroundColor = dark ? NSColor(hex: 0xDCDFE6) : NSColor(hex: 0x1F2329)
        view.caretColor = Palette.accent
        view.selectedTextBackgroundColor = Palette.selection
    }

    fileprivate func finished(_ code: Int32?) {
        isRunning = false
        exitCode = code
    }

    func terminate() {
        if isRunning {
            view.terminate()
        }
    }
}

/// Bridges SwiftTerm's delegate (called on the main thread) to the session.
@MainActor
private final class TerminalDelegate: NSObject, @preconcurrency LocalProcessTerminalViewDelegate {
    weak var session: TerminalSession?

    init(session: TerminalSession) {
        self.session = session
    }

    func sizeChanged(source: LocalProcessTerminalView, newCols: Int, newRows: Int) {}

    func setTerminalTitle(source: LocalProcessTerminalView, title: String) {
        if let session, !session.isRun, !title.isEmpty {
            session.title = title
        }
    }

    func hostCurrentDirectoryUpdate(source: TerminalView, directory: String?) {}

    func processTerminated(source: TerminalView, exitCode: Int32?) {
        session?.finished(exitCode)
    }
}

/// The terminal tabs of the bottom panel.
@MainActor
@Observable
final class TerminalController {
    @ObservationIgnored private unowned let workspace: Workspace
    private(set) var sessions: [TerminalSession] = []
    var activeID: UUID?

    init(workspace: Workspace) {
        self.workspace = workspace
    }

    var active: TerminalSession? { sessions.first { $0.id == activeID } }

    private var directory: URL {
        workspace.rootURL ?? FileManager.default.homeDirectoryForCurrentUser
    }

    private func environment(extra: [String: String] = [:]) -> [String] {
        var environment = (try? CommandRunner.dotnet())?.childEnvironment() ?? ProcessInfo.processInfo.environment
        environment["TERM"] = "xterm-256color"
        environment["COLORTERM"] = "truecolor"
        environment["TERM_PROGRAM"] = "VsLike"
        environment["LANG"] = environment["LANG"] ?? "en_US.UTF-8"
        for (key, value) in extra {
            environment[key] = value
        }
        return environment.map { "\($0.key)=\($0.value)" }
    }

    /// Opens a login shell in the solution folder.
    @discardableResult
    func newShell() -> TerminalSession {
        let shell = ProcessInfo.processInfo.environment["SHELL"] ?? "/bin/zsh"
        let session = TerminalSession(title: URL(fileURLWithPath: shell).lastPathComponent, isRun: false, font: terminalFont)
        session.view.startProcess(executable: shell, args: ["-l"], environment: environment(), execName: "-" + URL(fileURLWithPath: shell).lastPathComponent, currentDirectory: directory.path)
        add(session)
        return session
    }

    /// Starts `dotnet <arguments>` in a new tab (replacing a finished run tab with the same title).
    func run(title: String, arguments: [String], in directory: URL, environment extra: [String: String] = [:]) {
        guard let dotnet = DotnetLocator.locate() else {
            workspace.alert = AlertMessage(title: title, message: CommandError.dotnetNotFound.localizedDescription)
            return
        }
        for old in sessions where old.isRun && !old.isRunning && old.title == title {
            close(old)
        }
        let session = TerminalSession(title: title, isRun: true, font: terminalFont)
        session.view.startProcess(executable: dotnet.path, args: arguments, environment: environment(extra: extra), currentDirectory: directory.path)
        add(session)
    }

    private func add(_ session: TerminalSession) {
        sessions.append(session)
        activeID = session.id
        workspace.bottomTab = .terminal
        workspace.isBottomPanelVisible = true
        DispatchQueue.main.async {
            session.view.window?.makeFirstResponder(session.view)
        }
    }

    func close(_ session: TerminalSession) {
        session.terminate()
        sessions.removeAll { $0.id == session.id }
        if activeID == session.id {
            activeID = sessions.last?.id
        }
    }

    func stopRuns() {
        for session in sessions where session.isRun && session.isRunning {
            session.terminate()
        }
    }

    var hasRunningProgram: Bool { sessions.contains { $0.isRun && $0.isRunning } }

    private var terminalFont: NSFont {
        NSFont.monospacedSystemFont(ofSize: max(11, workspace.preferences.fontSize - 1), weight: .regular)
    }
}
