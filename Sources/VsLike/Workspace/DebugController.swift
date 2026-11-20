import Foundation
import Observation
import VsLikeKit

/// A variable in the Variables or Watch list; children load when expanded.
struct VariableNode: Identifiable, Hashable {
    let id: String
    let name: String
    let value: String
    let type: String?
    let reference: Int
    var children: [VariableNode]?
}

struct WatchExpression: Identifiable, Hashable {
    let id = UUID()
    var expression: String
    var value: String = ""
    var type: String?
    var isError = false
}

/// The debugger: builds, starts netcoredbg, keeps breakpoints in sync and shows where the program stopped.
@MainActor
@Observable
final class DebugController {
    @ObservationIgnored private unowned let workspace: Workspace
    @ObservationIgnored private var session: DebugSession?
    @ObservationIgnored private var eventTask: Task<Void, Never>?

    private(set) var isActive = false
    private(set) var isStarting = false
    private(set) var isStopped = false
    private(set) var stopReason: String?
    private(set) var threadID = 0
    private(set) var threads: [DebugThread] = []
    private(set) var frames: [StackFrame] = []
    var selectedFrameID: Int? {
        didSet {
            if oldValue != selectedFrameID, isStopped { Task { await loadFrame() } }
        }
    }
    private(set) var variables: [VariableNode] = []
    var watches: [WatchExpression] = []
    private(set) var console: [OutputLog.Line] = []
    @ObservationIgnored private var consoleID = 0

    init(workspace: Workspace) {
        self.workspace = workspace
    }

    var selectedFrame: StackFrame? { frames.first { $0.id == selectedFrameID } }

    func start() async {
        guard !isActive, !isStarting else {
            if isStopped { await resume() }
            return
        }
        guard let project = workspace.build.startupProject else {
            workspace.alert = AlertMessage(title: String(localized: "Debug"), message: String(localized: "This solution has no project that can be started."))
            return
        }
        guard let adapter = DebugSession.locateAdapter() else {
            workspace.alert = AlertMessage(title: String(localized: "Debug"), message: DebugError.adapterNotFound.localizedDescription)
            return
        }
        isStarting = true
        defer { isStarting = false }
        guard let result = await workspace.build.run(.build), result.succeeded else {
            return
        }
        do {
            let runner = try CommandRunner.dotnet(language: workspace.preferences.effectiveLanguage)
            let configuration = workspace.build.configuration
            guard let program = try await BuildService(runner: runner).targetPath(project: project, configuration: configuration) else {
                workspace.alert = AlertMessage(title: String(localized: "Debug"), message: String(localized: "The built program was not found."))
                return
            }
            let profile = workspace.build.profiles.first { $0.name == workspace.build.launchProfile }
            let arguments = profile?.commandLineArgs.map { $0.split(separator: " ").map(String.init) } ?? []
            var environment = profile?.environment ?? [:]
            if let url = profile?.applicationURL {
                environment["ASPNETCORE_URLS"] = url
            }
            let launch = LaunchConfiguration(
                program: program,
                arguments: arguments,
                workingDirectory: BuildService.workingDirectory(project: project, profile: profile).path,
                environment: environment
            )
            console = []
            appendConsole(String(localized: "Starting \(URL(fileURLWithPath: program).lastPathComponent) with netcoredbg…"))
            let (session, _) = try await DebugSession.start(adapter: adapter, launch: launch, breakpoints: workspace.allBreakpoints, dotnet: runner.executable)
            self.session = session
            isActive = true
            workspace.bottomTab = .debug
            workspace.isBottomPanelVisible = true
            workspace.status = StatusMessage(text: String(localized: "Debugging \(workspace.build.startupProjectName ?? "")"), style: .progress)
            eventTask = Task { [weak self] in
                for await event in session.events {
                    await self?.handle(event)
                }
            }
        } catch {
            workspace.alert = AlertMessage(title: String(localized: "Debug"), message: error.localizedDescription)
        }
    }

    private func handle(_ event: DebugEvent) async {
        switch event {
        case let .stopped(reason, thread, description):
            threadID = thread
            stopReason = description ?? reason
            await loadStop()
        case .continued:
            isStopped = false
            clearExecutionLine()
        case let .output(category, text):
            if category != "telemetry" {
                appendConsole(text.hasSuffix("\n") ? String(text.dropLast()) : text, isError: category == "stderr")
            }
        case let .exited(code):
            appendConsole(String(localized: "The program exited with code \(code)."))
        case .terminated:
            finish()
        }
    }

    /// Loads threads, stack, variables and watches, then marks the session as stopped and shows the line.
    private func loadStop() async {
        guard let session else { return }
        threads = (try? await session.threads()) ?? []
        frames = (try? await session.stackTrace(threadID: threadID)) ?? []
        let top = frames.first { $0.filePath != nil } ?? frames.first
        selectedFrameID = top?.id
        await loadFrame()
        isStopped = true
        workspace.status = StatusMessage(text: String(localized: "Paused: \(stopReason ?? "")"), style: .warning)
    }

    private func loadFrame() async {
        guard let session, let frame = selectedFrame else {
            variables = []
            return
        }
        var nodes: [VariableNode] = []
        for scope in (try? await session.scopes(frameID: frame.id)) ?? [] {
            for variable in (try? await session.variables(reference: scope.variablesReference)) ?? [] {
                nodes.append(node(variable, parent: scope.name))
            }
        }
        variables = nodes
        await refreshWatches()
        showExecutionLine(frame)
    }

    private func node(_ variable: DebugVariable, parent: String) -> VariableNode {
        VariableNode(
            id: parent + "/" + variable.name,
            name: variable.name,
            value: variable.value,
            type: variable.type,
            reference: variable.variablesReference,
            children: variable.variablesReference > 0 ? [] : nil
        )
    }

    /// Children of an expandable variable.
    func children(of variable: VariableNode) async -> [VariableNode] {
        guard let session, variable.reference > 0 else { return [] }
        return ((try? await session.variables(reference: variable.reference)) ?? []).map { node($0, parent: variable.id) }
    }

    func refreshWatches() async {
        guard let session else { return }
        for index in watches.indices {
            do {
                let result = try await session.evaluate(watches[index].expression, frameID: selectedFrameID, context: "watch")
                watches[index].value = result.value
                watches[index].type = result.type
                watches[index].isError = false
            } catch {
                watches[index].value = error.localizedDescription
                watches[index].isError = true
            }
        }
    }

    func addWatch(_ expression: String) {
        let trimmed = expression.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return }
        watches.append(WatchExpression(expression: trimmed))
        Task { await refreshWatches() }
    }

    /// Evaluates in the console (REPL).
    func evaluate(_ expression: String) async {
        appendConsole("› " + expression)
        guard let session, isStopped else {
            appendConsole(String(localized: "The program is not paused."), isError: true)
            return
        }
        do {
            let result = try await session.evaluate(expression, frameID: selectedFrameID, context: "repl")
            appendConsole(result.value + (result.type.map { "  (\($0))" } ?? ""))
        } catch {
            appendConsole(error.localizedDescription, isError: true)
        }
    }

    /// Value for hovering an identifier while paused.
    func hoverValue(_ expression: String) async -> String? {
        guard let session, isStopped, !expression.isEmpty else { return nil }
        guard let result = try? await session.evaluate(expression, frameID: selectedFrameID, context: "hover") else { return nil }
        return result.type.map { "\(expression) = \(result.value)  (\($0))" } ?? "\(expression) = \(result.value)"
    }

    func resume() async { await run { try await $0.resume(threadID: $1) } }

    func stepOver() async { await run { try await $0.stepOver(threadID: $1) } }

    func stepIn() async { await run { try await $0.stepIn(threadID: $1) } }

    func stepOut() async { await run { try await $0.stepOut(threadID: $1) } }

    func pause() async {
        guard let session, !isStopped else { return }
        let thread = threadID != 0 ? threadID : ((try? await session.threads())?.first?.id ?? 0)
        try? await session.pause(threadID: thread)
    }

    private func run(_ command: (DebugSession, Int) async throws -> Void) async {
        guard let session, isStopped else { return }
        isStopped = false
        clearExecutionLine()
        workspace.status = StatusMessage(text: String(localized: "Running…"), style: .progress)
        do {
            try await command(session, threadID)
        } catch {
            appendConsole(error.localizedDescription, isError: true)
        }
    }

    func stop() async {
        guard let session else { return }
        await session.stop()
        finish()
    }

    /// Sends the breakpoints of a file to the running session.
    func breakpointsChanged(in document: CodeDocument) {
        workspace.persistBreakpoints()
        guard let session, isActive else { return }
        let list = document.breakpoints.values.sorted { $0.line < $1.line }
        Task { _ = try? await session.setBreakpoints(file: document.path, list) }
    }

    private func finish() {
        guard isActive || session != nil else { return }
        eventTask?.cancel()
        session = nil
        isActive = false
        isStopped = false
        frames = []
        threads = []
        variables = []
        clearExecutionLine()
        workspace.status = StatusMessage(text: String(localized: "Debugging ended"), style: .neutral)
    }

    private func showExecutionLine(_ frame: StackFrame) {
        clearExecutionLine()
        guard let path = frame.filePath, FileManager.default.fileExists(atPath: path) else { return }
        if let document = workspace.openFile(URL(fileURLWithPath: path), line: frame.line, column: max(1, frame.column)) {
            document.executionLine = frame.line
        }
    }

    private func clearExecutionLine() {
        for document in workspace.openDocuments where document.executionLine != nil {
            document.executionLine = nil
        }
    }

    private func appendConsole(_ text: String, isError: Bool = false) {
        consoleID += 1
        console.append(OutputLog.Line(id: consoleID, channel: .debugger, text: text, isError: isError))
        if console.count > 5000 {
            console.removeFirst(console.count - 5000)
        }
    }
}
