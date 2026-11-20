import Foundation

public enum TestOutcome: String, Sendable {
    case notRun, running, passed, failed, skipped
}

public struct TestResult: Sendable, Hashable {
    public let fullName: String
    public let outcome: TestOutcome
    public let duration: Double
    public let errorMessage: String?
    public let stackTrace: String?
}

/// `Ns.Sub.Class.Method(args)` split into namespace, class and method.
public struct TestName: Sendable, Hashable {
    public let fullName: String
    public let namespace: String
    public let className: String
    public let method: String

    public init(_ fullName: String) {
        self.fullName = fullName
        let head = fullName.split(separator: "(", maxSplits: 1).first.map(String.init) ?? fullName
        let parts = head.split(separator: ".").map(String.init)
        switch parts.count {
        case 0, 1:
            namespace = ""
            className = ""
            method = parts.first ?? fullName
        case 2:
            namespace = ""
            className = parts[0]
            method = parts[1]
        default:
            namespace = parts.dropLast(2).joined(separator: ".")
            className = parts[parts.count - 2]
            method = parts[parts.count - 1]
        }
    }

    public var classFullName: String { namespace.isEmpty ? className : namespace + "." + className }

    public var methodFullName: String { classFullName.isEmpty ? method : classFullName + "." + method }

    /// The argument part of a data-driven case, e.g. `(x: 1)`.
    public var arguments: String { fullName.count > methodFullName.count ? String(fullName.dropFirst(methodFullName.count)) : "" }
}

/// Finds and runs tests with `dotnet test` (VSTest: xUnit, NUnit, MSTest). Results come from a TRX file.
public struct TestService: Sendable {
    public let runner: CommandRunner

    public init(runner: CommandRunner) {
        self.runner = runner
    }

    public func discover(project: String, configuration: String, onOutput: (@Sendable (OutputLine) -> Void)? = nil) async throws -> (tests: [String], process: ProcessResult) {
        let process = try await runner.run(
            ["test", project, "-c", configuration, "--list-tests", "-nologo", "-tl:off"],
            in: URL(fileURLWithPath: project).deletingLastPathComponent(),
            onOutput: onOutput
        )
        return (Self.parseTestList(process.output), process)
    }

    public func run(project: String, configuration: String, filter: String?, onOutput: (@Sendable (OutputLine) -> Void)? = nil) async throws -> (results: [TestResult], process: ProcessResult) {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("vslike-tests/" + UUID().uuidString)
        let trx = directory.appendingPathComponent("results.trx")
        defer { try? FileManager.default.removeItem(at: directory) }
        var arguments = ["test", project, "-c", configuration, "-nologo", "-tl:off", "--logger", "trx;LogFileName=" + trx.path, "--results-directory", directory.path]
        if let filter, !filter.isEmpty {
            arguments += ["--filter", filter]
        }
        let process = try await runner.run(arguments, in: URL(fileURLWithPath: project).deletingLastPathComponent(), onOutput: onOutput)
        let results = (try? Data(contentsOf: trx)).map(Self.parseTrx) ?? []
        return (results, process)
    }

    /// Test names follow the (localized) header line ending with a colon and are indented by four spaces.
    public static func parseTestList(_ output: [OutputLine]) -> [String] {
        let lines = output.filter { !$0.isError }.map(\.text)
        guard let header = lines.lastIndex(where: { line in
            guard let first = line.first else { return false }
            return !first.isWhitespace && line.trimmingCharacters(in: .whitespaces).hasSuffix(":")
        }) else {
            return []
        }
        var seen = Set<String>()
        return lines[(header + 1)...]
            .prefix { $0.hasPrefix("    ") }
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && seen.insert($0).inserted }
    }

    public static func parseTrx(_ data: Data) -> [TestResult] {
        guard let document = try? XMLDocument(data: data) else {
            return []
        }
        let nodes = (try? document.nodes(forXPath: "//*[local-name()='UnitTestResult']")) ?? []
        return nodes.compactMap { node -> TestResult? in
            guard let element = node as? XMLElement else {
                return nil
            }
            let error = (try? element.nodes(forXPath: "*[local-name()='Output']/*[local-name()='ErrorInfo']"))?.first as? XMLElement
            let outcome: TestOutcome = switch element.attribute(forName: "outcome")?.stringValue {
            case "Passed": .passed
            case "Failed", "Error", "Timeout", "Aborted": .failed
            case "NotExecuted", "Inconclusive", "Skipped": .skipped
            default: .notRun
            }
            return TestResult(
                fullName: element.attribute(forName: "testName")?.stringValue ?? "",
                outcome: outcome,
                duration: parseDuration(element.attribute(forName: "duration")?.stringValue),
                errorMessage: (try? error?.nodes(forXPath: "*[local-name()='Message']"))?.first?.stringValue,
                stackTrace: (try? error?.nodes(forXPath: "*[local-name()='StackTrace']"))?.first?.stringValue
            )
        }
    }

    /// `hh:mm:ss.fffffff` in seconds.
    static func parseDuration(_ text: String?) -> Double {
        guard let parts = text?.split(separator: ":"), parts.count == 3,
              let hours = Double(parts[0]), let minutes = Double(parts[1]), let seconds = Double(parts[2]) else {
            return 0
        }
        return hours * 3600 + minutes * 60 + seconds
    }

    /// First frame in the test's own source (`in /path/File.cs:line 42`).
    public static func firstSourceFrame(_ stackTrace: String?) -> (file: String, line: Int)? {
        for line in (stackTrace ?? "").split(separator: "\n") {
            let text = String(line).trimmingCharacters(in: CharacterSet(charactersIn: "\r"))
            guard let marker = text.range(of: ":line ", options: .backwards), let inRange = text.range(of: " in ", options: .backwards),
                  inRange.upperBound <= marker.lowerBound, let number = Int(text[marker.upperBound...].trimmingCharacters(in: .whitespaces)) else {
                continue
            }
            return (String(text[inRange.upperBound..<marker.lowerBound]), number)
        }
        return nil
    }

    /// A VSTest filter: exact matches for single tests, prefix matches (`~Ns.Class.`) for groups.
    public static func filter(exact: [String], prefixes: [String]) -> String {
        let exactParts = exact.map { "FullyQualifiedName=" + escape(String($0.split(separator: "(", maxSplits: 1).first ?? Substring($0))) }
        return (exactParts + prefixes.map { "FullyQualifiedName~" + escape($0) }).joined(separator: "|")
    }

    static func escape(_ value: String) -> String {
        var result = ""
        for character in value {
            if "\\()&|=!~".contains(character) {
                result.append("\\")
            }
            result.append(character)
        }
        return result
    }
}
