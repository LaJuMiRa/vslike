import Foundation

public struct SourceBreakpoint: Sendable, Hashable, Codable {
    public let line: Int
    public let condition: String?

    public init(line: Int, condition: String? = nil) {
        self.line = line
        self.condition = condition
    }
}

public struct BreakpointState: Sendable, Hashable {
    public let line: Int
    public let verified: Bool
    public let message: String?
}

public struct DebugThread: Sendable, Hashable, Identifiable {
    public let id: Int
    public let name: String
}

public struct StackFrame: Sendable, Hashable, Identifiable {
    public let id: Int
    public let name: String
    public let filePath: String?
    public let line: Int
    public let column: Int
}

public struct DebugScope: Sendable, Hashable {
    public let name: String
    public let variablesReference: Int
}

public struct DebugVariable: Sendable, Hashable, Identifiable {
    public let name: String
    public let value: String
    public let type: String?
    public let variablesReference: Int

    public var id: String { "\(name)#\(variablesReference)#\(value)" }
}

public struct LaunchConfiguration: Sendable {
    public let program: String
    public let arguments: [String]
    public let workingDirectory: String
    public let environment: [String: String]
    public let stopAtEntry: Bool

    public init(program: String, arguments: [String] = [], workingDirectory: String, environment: [String: String] = [:], stopAtEntry: Bool = false) {
        self.program = program
        self.arguments = arguments
        self.workingDirectory = workingDirectory
        self.environment = environment
        self.stopAtEntry = stopAtEntry
    }
}

public enum DebugEvent: Sendable {
    case stopped(reason: String, threadID: Int, description: String?)
    case continued
    case output(category: String, text: String)
    case exited(code: Int)
    case terminated
}

public enum DebugError: Error, LocalizedError, Sendable {
    case adapterNotFound
    case adapterFailed(String)
    case request(command: String, message: String)

    public var errorDescription: String? {
        switch self {
        case .adapterNotFound: return "The debugger (netcoredbg) was not found in the app bundle."
        case let .adapterFailed(message): return "The debugger could not be started: \(message)"
        case let .request(command, message): return "\(command): \(message)"
        }
    }
}

/// A debug session with netcoredbg over the Debug Adapter Protocol (`--interpreter=vscode`). Events arrive on
/// `events`; requests are async. vsdbg is never used (its licence forbids it).
public final class DebugSession: @unchecked Sendable {
    public let events: AsyncStream<DebugEvent>

    private let process = Process()
    private let lock = NSLock()
    private let writeLock = NSLock()
    private let eventContinuation: AsyncStream<DebugEvent>.Continuation
    private var input: FileHandle?
    private var framer = MessageFramer()
    private var nextSeq = 0
    private var pending: [Int: CheckedContinuation<JSONValue, Error>] = [:]
    private var initialized: CheckedContinuation<Void, Never>?
    private var isInitialized = false

    private init() {
        (events, eventContinuation) = AsyncStream.makeStream(bufferingPolicy: .unbounded)
    }

    /// Bundled netcoredbg (`Contents/Resources/netcoredbg/netcoredbg`), or `VSLIKE_NETCOREDBG` for development.
    public static func locateAdapter(bundle: Bundle = .main) -> URL? {
        var candidates: [String] = []
        if let override = ProcessInfo.processInfo.environment["VSLIKE_NETCOREDBG"] {
            candidates.append(override)
        }
        if let resources = bundle.resourceURL {
            candidates.append(resources.appendingPathComponent("netcoredbg/netcoredbg").path)
        }
        let cache = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Caches/VsLike/netcoredbg")
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: cache.path) {
            candidates += versions.sorted().reversed().map { cache.appendingPathComponent("\($0)/netcoredbg/netcoredbg").path }
        }
        return candidates.map(URL.init(fileURLWithPath:)).first { FileManager.default.isExecutableFile(atPath: $0.path) }
    }

    /// Starts netcoredbg, launches the program, sets the breakpoints and runs. Subscribe to `events` right after
    /// this returns; events that arrived earlier are buffered. The program's output arrives as `.output` events and
    /// it cannot read input; `attach` is the variant for programs that run in a terminal.
    public static func start(
        adapter: URL,
        launch: LaunchConfiguration,
        breakpoints: [String: [SourceBreakpoint]],
        dotnet: URL?
    ) async throws -> (session: DebugSession, breakpoints: [String: [BreakpointState]]) {
        var environment: [String: JSONValue] = [:]
        for (key, value) in launch.environment {
            environment[key] = .string(value)
        }
        return try await connect(adapter: adapter, dotnet: dotnet, breakpoints: breakpoints, command: "launch", arguments: [
            "name": "VsLike", "type": "coreclr", "request": "launch", "program": .string(launch.program),
            "args": .strings(launch.arguments), "cwd": .string(launch.workingDirectory), "env": .object(environment),
            "stopAtEntry": .bool(launch.stopAtEntry), "console": "internalConsole", "justMyCode": true,
        ])
    }

    /// Attaches netcoredbg to a running .NET process (a program started in a terminal), sets the breakpoints and
    /// then creates `readyFile`, which releases a program held by the debug startup hook (`DebugStartupHook`).
    public static func attach(
        adapter: URL,
        processID: Int32,
        breakpoints: [String: [SourceBreakpoint]],
        dotnet: URL?,
        readyFile: URL?
    ) async throws -> (session: DebugSession, breakpoints: [String: [BreakpointState]]) {
        let result = try await connect(adapter: adapter, dotnet: dotnet, breakpoints: breakpoints, command: "attach", arguments: [
            "name": "VsLike", "type": "coreclr", "request": "attach", "processId": .int(Int(processID)), "justMyCode": true,
        ])
        if let readyFile {
            FileManager.default.createFile(atPath: readyFile.path, contents: nil)
        }
        return result
    }

    private static func connect(
        adapter: URL,
        dotnet: URL?,
        breakpoints: [String: [SourceBreakpoint]],
        command: String,
        arguments: JSONValue
    ) async throws -> (session: DebugSession, breakpoints: [String: [BreakpointState]]) {
        let session = DebugSession()
        try session.spawn(adapter: adapter, dotnet: dotnet)
        do {
            _ = try await session.request("initialize", [
                "clientID": "vslike", "clientName": "VsLike", "adapterID": "coreclr", "linesStartAt1": true,
                "columnsStartAt1": true, "pathFormat": "path", "supportsVariableType": true,
            ])
            let startTask = Task { try await session.request(command, arguments) }
            try await session.waitForInitialized(timeout: 30)
            var states: [String: [BreakpointState]] = [:]
            for (file, list) in breakpoints {
                states[file] = try await session.setBreakpoints(file: file, list)
            }
            _ = try await session.request("configurationDone", .null)
            _ = try await startTask.value
            return (session, states)
        } catch {
            session.kill()
            throw error
        }
    }

    public func setBreakpoints(file: String, _ breakpoints: [SourceBreakpoint]) async throws -> [BreakpointState] {
        let list: [JSONValue] = breakpoints.map { breakpoint in
            var entry: [String: JSONValue] = ["line": .int(breakpoint.line)]
            if let condition = breakpoint.condition, !condition.trimmingCharacters(in: .whitespaces).isEmpty {
                entry["condition"] = .string(condition)
            }
            return .object(entry)
        }
        let body = try await request("setBreakpoints", [
            "source": ["path": .string(file), "name": .string(URL(fileURLWithPath: file).lastPathComponent)],
            "breakpoints": .array(list),
        ])
        return body["breakpoints"]?.arrayValue.enumerated().map { index, item in
            BreakpointState(
                line: item["line"]?.intValue ?? breakpoints[safe: index]?.line ?? 0,
                verified: item["verified"]?.boolValue ?? false,
                message: item["message"]?.stringValue
            )
        } ?? []
    }

    public func threads() async throws -> [DebugThread] {
        try await request("threads", .null)["threads"]?.arrayValue.map {
            DebugThread(id: $0["id"]?.intValue ?? 0, name: $0["name"]?.stringValue ?? "")
        } ?? []
    }

    public func stackTrace(threadID: Int) async throws -> [StackFrame] {
        try await request("stackTrace", ["threadId": .int(threadID), "startFrame": 0, "levels": 100])["stackFrames"]?.arrayValue.map {
            StackFrame(
                id: $0["id"]?.intValue ?? 0,
                name: $0["name"]?.stringValue ?? "",
                filePath: $0["source"]?["path"]?.stringValue,
                line: $0["line"]?.intValue ?? 0,
                column: $0["column"]?.intValue ?? 0
            )
        } ?? []
    }

    public func scopes(frameID: Int) async throws -> [DebugScope] {
        try await request("scopes", ["frameId": .int(frameID)])["scopes"]?.arrayValue.map {
            DebugScope(name: $0["name"]?.stringValue ?? "", variablesReference: $0["variablesReference"]?.intValue ?? 0)
        } ?? []
    }

    public func variables(reference: Int) async throws -> [DebugVariable] {
        try await request("variables", ["variablesReference": .int(reference)])["variables"]?.arrayValue.map {
            DebugVariable(
                name: $0["name"]?.stringValue ?? "",
                value: $0["value"]?.stringValue ?? "",
                type: $0["type"]?.stringValue,
                variablesReference: $0["variablesReference"]?.intValue ?? 0
            )
        } ?? []
    }

    /// - Parameter context: "watch", "hover" or "repl".
    public func evaluate(_ expression: String, frameID: Int?, context: String) async throws -> DebugVariable {
        var arguments: [String: JSONValue] = ["expression": .string(expression), "context": .string(context)]
        if let frameID {
            arguments["frameId"] = .int(frameID)
        }
        let body = try await request("evaluate", .object(arguments))
        return DebugVariable(
            name: expression,
            value: body["result"]?.stringValue ?? "",
            type: body["type"]?.stringValue,
            variablesReference: body["variablesReference"]?.intValue ?? 0
        )
    }

    public func resume(threadID: Int) async throws { try await step("continue", threadID) }

    public func stepOver(threadID: Int) async throws { try await step("next", threadID) }

    public func stepIn(threadID: Int) async throws { try await step("stepIn", threadID) }

    public func stepOut(threadID: Int) async throws { try await step("stepOut", threadID) }

    public func pause(threadID: Int) async throws {
        _ = try await request("pause", ["threadId": .int(threadID)])
    }

    /// Ends the debuggee and the adapter.
    public func stop() async {
        _ = try? await withThrowingTaskGroup(of: Void.self) { group in
            group.addTask { _ = try await self.request("disconnect", ["terminateDebuggee": true]) }
            group.addTask { try await Task.sleep(for: .seconds(3)) }
            try await group.next()
            group.cancelAll()
        }
        kill()
    }

    public func kill() {
        if process.isRunning {
            process.terminate()
        }
    }

    private func step(_ command: String, _ threadID: Int) async throws {
        eventContinuation.yield(.continued)
        _ = try await request(command, ["threadId": .int(threadID)])
    }

    private func spawn(adapter: URL, dotnet: URL?) throws {
        process.executableURL = adapter
        process.arguments = ["--interpreter=vscode"]
        var environment = ProcessInfo.processInfo.environment.filter { key, _ in !key.lowercased().hasPrefix("msbuild") && !key.hasPrefix("VSTEST") }
        if let dotnet {
            let folder = dotnet.deletingLastPathComponent().path
            environment["PATH"] = folder + ":" + (environment["PATH"] ?? "/usr/bin:/bin")
            environment["DOTNET_ROOT"] = folder
        }
        process.environment = environment
        let stdin = Pipe()
        let stdout = Pipe()
        process.standardInput = stdin
        process.standardOutput = stdout
        process.standardError = FileHandle.nullDevice
        input = stdin.fileHandleForWriting
        stdout.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            if data.isEmpty {
                handle.readabilityHandler = nil
            }
            self?.receive(data)
        }
        process.terminationHandler = { [weak self] _ in
            self?.adapterExited()
        }
        do {
            try process.run()
        } catch {
            throw DebugError.adapterFailed(error.localizedDescription)
        }
    }

    private func waitForInitialized(timeout: TimeInterval) async throws {
        try await withThrowingTaskGroup(of: Void.self) { group in
            group.addTask {
                await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                    let alreadyInitialized = self.lock.withLock { () -> Bool in
                        if self.isInitialized {
                            return true
                        }
                        self.initialized = continuation
                        return false
                    }
                    if alreadyInitialized {
                        continuation.resume()
                    }
                }
            }
            group.addTask {
                try await Task.sleep(for: .seconds(timeout))
                throw DebugError.adapterFailed("no 'initialized' event within \(Int(timeout)) s")
            }
            try await group.next()
            group.cancelAll()
        }
    }

    @discardableResult
    private func request(_ command: String, _ arguments: JSONValue) async throws -> JSONValue {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<JSONValue, Error>) in
            let (seq, handle) = lock.withLock { () -> (Int, FileHandle?) in
                nextSeq += 1
                if input != nil {
                    pending[nextSeq] = continuation
                }
                return (nextSeq, input)
            }
            guard let handle else {
                continuation.resume(throwing: DebugError.request(command: command, message: "debugger not running"))
                return
            }
            var message: [String: JSONValue] = ["seq": .int(seq), "type": "request", "command": .string(command)]
            if arguments != .null {
                message["arguments"] = arguments
            }
            if let body = try? JSONEncoder().encode(JSONValue.object(message)) {
                writeLock.withLock { try? handle.write(contentsOf: MessageFramer.frame(body)) }
            }
        }
    }

    private func receive(_ data: Data) {
        let messages = lock.withLock { framer.append(data) }
        for body in messages {
            guard let message = try? JSONDecoder().decode(JSONValue.self, from: body) else {
                continue
            }
            switch message["type"]?.stringValue {
            case "response":
                guard let seq = message["request_seq"]?.intValue, let continuation = lock.withLock({ pending.removeValue(forKey: seq) }) else {
                    continue
                }
                if message["success"]?.boolValue == true {
                    continuation.resume(returning: message["body"] ?? .null)
                } else {
                    let text = message["body"]?["error"]?["format"]?.stringValue ?? message["message"]?.stringValue ?? "failed"
                    continuation.resume(throwing: DebugError.request(command: message["command"]?.stringValue ?? "", message: text))
                }
            case "event":
                handle(event: message["event"]?.stringValue ?? "", body: message["body"] ?? .null)
            default:
                continue
            }
        }
    }

    private func handle(event: String, body: JSONValue) {
        switch event {
        case "initialized":
            let waiter = lock.withLock { () -> CheckedContinuation<Void, Never>? in
                isInitialized = true
                defer { initialized = nil }
                return initialized
            }
            waiter?.resume()
        case "stopped":
            eventContinuation.yield(.stopped(reason: body["reason"]?.stringValue ?? "", threadID: body["threadId"]?.intValue ?? 0, description: body["description"]?.stringValue))
        case "continued":
            eventContinuation.yield(.continued)
        case "output":
            eventContinuation.yield(.output(category: body["category"]?.stringValue ?? "console", text: body["output"]?.stringValue ?? ""))
        case "exited":
            eventContinuation.yield(.exited(code: body["exitCode"]?.intValue ?? 0))
        case "terminated":
            eventContinuation.yield(.terminated)
        default:
            break
        }
    }

    private func adapterExited() {
        let waiting = lock.withLock { () -> [CheckedContinuation<JSONValue, Error>] in
            input = nil
            defer { pending.removeAll() }
            return Array(pending.values)
        }
        waiting.forEach { $0.resume(throwing: DebugError.request(command: "", message: "debugger ended")) }
        eventContinuation.yield(.terminated)
        eventContinuation.finish()
    }
}

extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}
