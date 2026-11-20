import Foundation

public enum EngineError: Error, LocalizedError, Sendable {
    case notRunning
    case engineNotFound(String)
    case remote(code: Int, message: String)
    case invalidResponse(String)

    public var errorDescription: String? {
        switch self {
        case .notRunning: return "The analysis engine is not running."
        case let .engineNotFound(path): return "The analysis engine was not found at \(path)."
        case let .remote(_, message): return message
        case let .invalidResponse(detail): return "Invalid response from the analysis engine: \(detail)"
        }
    }

    /// The engine reports handled failures (e.g. "type not found") with code 1.
    public var isOperationFailure: Bool {
        if case let .remote(code, _) = self { return code == 1 }
        return false
    }
}

/// A notification pushed by the engine, e.g. `workspace/diagnostics`.
public struct EngineNotification: Sendable {
    public let method: String
    public let params: JSONValue
}

/// JSON-RPC client of the `VsLikeEngine` process (Roslyn, StyleCop, diagram writer). Requests are async and
/// cancellable; notifications arrive on `notifications`. The process is started with the installed `dotnet`.
public final class EngineClient: @unchecked Sendable {
    public let notifications: AsyncStream<EngineNotification>

    private let engineDLL: URL
    private let runner: CommandRunner
    private let lock = NSLock()
    private let writeLock = NSLock()
    private let notificationContinuation: AsyncStream<EngineNotification>.Continuation
    private var process: Process?
    private var input: FileHandle?
    private var framer = MessageFramer()
    private var nextID = 0
    private var pending: [Int: CheckedContinuation<JSONValue, Error>] = [:]
    private var onExit: (@Sendable (Int32) -> Void)?
    private var stderrTail: [String] = []

    public init(engineDLL: URL, runner: CommandRunner) {
        self.engineDLL = engineDLL
        self.runner = runner
        (notifications, notificationContinuation) = AsyncStream.makeStream(bufferingPolicy: .unbounded)
    }

    deinit {
        process?.terminate()
    }

    public var isRunning: Bool { lock.withLock { process?.isRunning ?? false } }

    /// The last lines the engine wrote to stderr (for error reports).
    public var recentLog: String { lock.withLock { stderrTail.joined(separator: "\n") } }

    /// Starts the process. `onExit` is called when it ends unexpectedly or after `stop()`.
    public func start(onExit: (@Sendable (Int32) -> Void)? = nil) throws {
        guard FileManager.default.fileExists(atPath: engineDLL.path) else {
            throw EngineError.engineNotFound(engineDLL.path)
        }
        let process = Process()
        process.executableURL = runner.executable
        process.arguments = [engineDLL.path]
        process.environment = runner.childEnvironment()
        let stdin = Pipe()
        let stdout = Pipe()
        let stderr = Pipe()
        process.standardInput = stdin
        process.standardOutput = stdout
        process.standardError = stderr
        stdout.fileHandleForReading.readabilityHandler = { [weak self] handle in
            self?.receive(handle.availableData)
        }
        stderr.fileHandleForReading.readabilityHandler = { [weak self] handle in
            self?.receiveLog(handle.availableData)
        }
        process.terminationHandler = { [weak self] finished in
            stdout.fileHandleForReading.readabilityHandler = nil
            stderr.fileHandleForReading.readabilityHandler = nil
            self?.terminated(status: finished.terminationStatus)
        }
        lock.withLock {
            self.onExit = onExit
            self.process = process
            self.input = stdin.fileHandleForWriting
        }
        try process.run()
    }

    public func stop() async {
        if isRunning {
            _ = try? await rawRequest("shutdown", params: .null, timeout: 5)
        }
        let running = lock.withLock { process }
        if let running, running.isRunning {
            running.terminate()
        }
    }

    /// Sends a request and decodes its `result`.
    public func request<R: Decodable>(_ method: String, _ params: some Encodable, as type: R.Type = R.self, timeout: TimeInterval? = nil) async throws -> R {
        let result = try await rawRequest(method, params: JSONValue.encode(params), timeout: timeout)
        do {
            return try result.decode(R.self)
        } catch {
            throw EngineError.invalidResponse("\(method): \(error)")
        }
    }

    public func rawRequest(_ method: String, params: JSONValue, timeout: TimeInterval? = nil) async throws -> JSONValue {
        let id = lock.withLock { () -> Int in
            nextID += 1
            return nextID
        }
        let message: JSONValue = ["jsonrpc": "2.0", "id": .int(id), "method": .string(method), "params": params]
        let timeoutTask = timeout.map { seconds in
            Task { [weak self] in
                try await Task.sleep(for: .seconds(seconds))
                self?.fail(id: id, with: EngineError.remote(code: -32800, message: "\(method) timed out after \(Int(seconds)) s."))
            }
        }
        defer { timeoutTask?.cancel() }
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<JSONValue, Error>) in
                let handle = lock.withLock { () -> FileHandle? in
                    guard let input, process?.isRunning == true else {
                        return nil
                    }
                    pending[id] = continuation
                    return input
                }
                guard let handle else {
                    continuation.resume(throwing: EngineError.notRunning)
                    return
                }
                write(message, to: handle)
            }
        } onCancel: { [weak self] in
            self?.notify("$/cancelRequest", ["id": .int(id)])
            self?.fail(id: id, with: CancellationError())
        }
    }

    /// Sends a notification (no response). Notifications keep their order in the engine.
    public func notify(_ method: String, _ params: JSONValue) {
        guard let handle = lock.withLock({ input }) else {
            return
        }
        write(["jsonrpc": "2.0", "method": .string(method), "params": params], to: handle)
    }

    private func write(_ message: JSONValue, to handle: FileHandle) {
        guard let body = try? JSONEncoder().encode(message) else {
            return
        }
        writeLock.withLock {
            try? handle.write(contentsOf: MessageFramer.frame(body))
        }
    }

    private func fail(id: Int, with error: Error) {
        let continuation = lock.withLock { pending.removeValue(forKey: id) }
        continuation?.resume(throwing: error)
    }

    private func receive(_ data: Data) {
        guard !data.isEmpty else {
            return
        }
        let messages = lock.withLock { framer.append(data) }
        for body in messages {
            guard let message = try? JSONDecoder().decode(JSONValue.self, from: body) else {
                continue
            }
            if let id = message["id"]?.intValue, message["method"] == nil {
                let continuation = lock.withLock { pending.removeValue(forKey: id) }
                if let error = message["error"] {
                    continuation?.resume(throwing: EngineError.remote(code: error["code"]?.intValue ?? -1, message: error["message"]?.stringValue ?? "error"))
                } else {
                    continuation?.resume(returning: message["result"] ?? .null)
                }
            } else if let method = message["method"]?.stringValue {
                notificationContinuation.yield(EngineNotification(method: method, params: message["params"] ?? .null))
            }
        }
    }

    private func receiveLog(_ data: Data) {
        let lines = String(decoding: data, as: UTF8.self).split(separator: "\n").map(String.init)
        lock.withLock {
            stderrTail.append(contentsOf: lines)
            if stderrTail.count > 200 {
                stderrTail.removeFirst(stderrTail.count - 200)
            }
        }
    }

    private func terminated(status: Int32) {
        let (waiting, callback) = lock.withLock { () -> ([CheckedContinuation<JSONValue, Error>], (@Sendable (Int32) -> Void)?) in
            let waiting = Array(pending.values)
            pending.removeAll()
            input = nil
            return (waiting, onExit)
        }
        waiting.forEach { $0.resume(throwing: EngineError.notRunning) }
        callback?(status)
    }
}
