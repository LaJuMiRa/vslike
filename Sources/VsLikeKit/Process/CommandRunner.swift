import Foundation

/// One line of process output; `isError` marks lines from stderr.
public struct OutputLine: Sendable, Hashable {
    public let text: String
    public let isError: Bool

    public init(_ text: String, isError: Bool = false) {
        self.text = text
        self.isError = isError
    }
}

public struct ProcessResult: Sendable {
    public let exitCode: Int32
    public let output: [OutputLine]
    public let wasCancelled: Bool

    public init(exitCode: Int32, output: [OutputLine], wasCancelled: Bool) {
        self.exitCode = exitCode
        self.output = output
        self.wasCancelled = wasCancelled
    }

    public var succeeded: Bool { exitCode == 0 && !wasCancelled }

    public var standardOutput: String { output.filter { !$0.isError }.map(\.text).joined(separator: "\n") }

    public var allText: String { output.map(\.text).joined(separator: "\n") }
}

public enum CommandError: Error, LocalizedError, Sendable {
    case dotnetNotFound
    case failed(command: String, exitCode: Int32, output: String)

    public var errorDescription: String? {
        switch self {
        case .dotnetNotFound:
            return "The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download."
        case let .failed(command, exitCode, output):
            let tail = output.split(separator: "\n").suffix(15).joined(separator: "\n")
            return "\(command) failed (exit \(exitCode)).\n\(tail)"
        }
    }
}

/// Runs a command line tool (normally `dotnet`) with live output, line by line, and cancellation through the
/// surrounding task.
public struct CommandRunner: Sendable {
    public let executable: URL
    public let environment: [String: String]

    public init(executable: URL, environment: [String: String] = [:]) {
        self.executable = executable
        self.environment = environment
    }

    /// The `dotnet` CLI with telemetry and logo off and without MSBuild variables inherited from a test host.
    public static func dotnet(language: String? = nil) throws -> CommandRunner {
        guard let path = DotnetLocator.locate() else {
            throw CommandError.dotnetNotFound
        }
        var environment = ["DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"]
        if let language {
            environment["DOTNET_CLI_UI_LANGUAGE"] = language
        }
        return CommandRunner(executable: path, environment: environment)
    }

    /// Environment for child processes: the current one without inherited build variables, plus `environment`, plus
    /// the dotnet folder on `PATH` (apps started from Finder get a minimal `PATH`).
    public func childEnvironment(extra: [String: String] = [:]) -> [String: String] {
        var result = ProcessInfo.processInfo.environment.filter { key, _ in
            !(key.lowercased().hasPrefix("msbuild") || key.hasPrefix("VSTEST") || key == "DOTNET_HOST_PATH")
        }
        for (key, value) in environment.merging(extra, uniquingKeysWith: { $1 }) {
            result[key] = value
        }
        let folder = executable.deletingLastPathComponent().path
        let path = result["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin"
        if !path.split(separator: ":").contains(Substring(folder)) {
            result["PATH"] = folder + ":" + path
        }
        return result
    }

    @discardableResult
    public func run(
        _ arguments: [String],
        in directory: URL? = nil,
        environment extra: [String: String] = [:],
        onOutput: (@Sendable (OutputLine) -> Void)? = nil
    ) async throws -> ProcessResult {
        let process = Process()
        process.executableURL = executable
        process.arguments = arguments
        process.environment = childEnvironment(extra: extra)
        if let directory {
            process.currentDirectoryURL = directory
        }
        let collector = OutputCollector(onOutput: onOutput)
        let stdout = Pipe()
        let stderr = Pipe()
        process.standardOutput = stdout
        process.standardError = stderr
        process.standardInput = FileHandle.nullDevice
        stdout.fileHandleForReading.readabilityHandler = { handle in collector.receive(handle.availableData, isError: false) }
        stderr.fileHandleForReading.readabilityHandler = { handle in collector.receive(handle.availableData, isError: true) }

        let cancelled = CancelFlag()
        let exitCode: Int32 = try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Int32, Error>) in
                process.terminationHandler = { finished in
                    continuation.resume(returning: finished.terminationStatus)
                }
                do {
                    try process.run()
                } catch {
                    process.terminationHandler = nil
                    continuation.resume(throwing: error)
                }
            }
        } onCancel: {
            cancelled.set()
            if process.isRunning {
                process.terminate()
            }
        }
        stdout.fileHandleForReading.readabilityHandler = nil
        stderr.fileHandleForReading.readabilityHandler = nil
        collector.receive(stdout.fileHandleForReading.readDataToEndOfFile(), isError: false)
        collector.receive(stderr.fileHandleForReading.readDataToEndOfFile(), isError: true)
        collector.flush()
        return ProcessResult(exitCode: exitCode, output: collector.lines, wasCancelled: cancelled.isSet)
    }

    /// Runs and throws `CommandError.failed` when the exit code is not 0.
    @discardableResult
    public func runChecked(_ arguments: [String], in directory: URL? = nil) async throws -> ProcessResult {
        let result = try await run(arguments, in: directory)
        if !result.succeeded {
            throw CommandError.failed(command: "dotnet " + arguments.joined(separator: " "), exitCode: result.exitCode, output: result.allText)
        }
        return result
    }
}

/// Thread-safe line splitter for both pipes of a process.
final class OutputCollector: @unchecked Sendable {
    private let lock = NSLock()
    private let onOutput: (@Sendable (OutputLine) -> Void)?
    private var partial: [Bool: Data] = [:]
    private var collected: [OutputLine] = []

    init(onOutput: (@Sendable (OutputLine) -> Void)?) {
        self.onOutput = onOutput
    }

    var lines: [OutputLine] {
        lock.withLock { collected }
    }

    func receive(_ data: Data, isError: Bool) {
        guard !data.isEmpty else {
            return
        }
        var emitted: [OutputLine] = []
        lock.withLock {
            var buffer = partial[isError, default: Data()]
            buffer.append(data)
            while let newline = buffer.firstIndex(of: UInt8(ascii: "\n")) {
                var lineData = buffer[buffer.startIndex..<newline]
                if lineData.last == UInt8(ascii: "\r") {
                    lineData = lineData.dropLast()
                }
                emitted.append(OutputLine(String(decoding: lineData, as: UTF8.self), isError: isError))
                buffer.removeSubrange(buffer.startIndex...newline)
            }
            partial[isError] = buffer
            collected.append(contentsOf: emitted)
        }
        emitted.forEach { onOutput?($0) }
    }

    func flush() {
        var emitted: [OutputLine] = []
        lock.withLock {
            for (isError, buffer) in partial where !buffer.isEmpty {
                emitted.append(OutputLine(String(decoding: buffer, as: UTF8.self), isError: isError))
            }
            partial = [:]
            collected.append(contentsOf: emitted)
        }
        emitted.forEach { onOutput?($0) }
    }
}

final class CancelFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var value = false

    var isSet: Bool { lock.withLock { value } }

    func set() {
        lock.withLock { value = true }
    }
}
