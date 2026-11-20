import Foundation

public enum BuildKind: Sendable {
    case build, rebuild, clean
}

public struct BuildResult: Sendable {
    public let process: ProcessResult
    public let diagnostics: [Diagnostic]
    public let duration: Duration

    public var succeeded: Bool { process.succeeded }
    public var errorCount: Int { diagnostics.filter { $0.severity == .error }.count }
    public var warningCount: Int { diagnostics.filter { $0.severity == .warning }.count }
}

/// A launch profile from `Properties/launchSettings.json`.
public struct LaunchProfile: Sendable, Hashable, Identifiable {
    public let name: String
    public let commandName: String
    public let commandLineArgs: String?
    public let workingDirectory: String?
    public let applicationURL: String?
    public let environment: [String: String]

    public var id: String { name }

    /// `dotnet run` can start "Project" and "Executable" profiles (not IIS or Docker ones).
    public var isRunnable: Bool { ["project", "executable"].contains(commandName.lowercased()) }

    public static func file(for project: String) -> URL {
        URL(fileURLWithPath: project).deletingLastPathComponent().appendingPathComponent("Properties/launchSettings.json")
    }

    /// Profiles of a project; a missing or broken file gives an empty list.
    public static func read(project: String) -> [LaunchProfile] {
        guard let data = try? Data(contentsOf: file(for: project)) else {
            return []
        }
        return parse(data)
    }

    public static func parse(_ data: Data) -> [LaunchProfile] {
        // launchSettings.json may contain comments and trailing commas.
        let cleaned = String(decoding: data, as: UTF8.self)
            .replacingOccurrences(of: #"(?m)^\s*//.*$"#, with: "", options: .regularExpression)
            .replacingOccurrences(of: #",(\s*[}\]])"#, with: "$1", options: .regularExpression)
        guard let root = try? JSONSerialization.jsonObject(with: Data(cleaned.utf8)) as? [String: Any],
              let profiles = root["profiles"] as? [String: Any] else {
            return []
        }
        return profiles.keys.sorted().compactMap { name in
            guard let value = profiles[name] as? [String: Any] else {
                return nil
            }
            let environment = (value["environmentVariables"] as? [String: Any] ?? [:]).mapValues { "\($0)" }
            return LaunchProfile(
                name: name,
                commandName: value["commandName"] as? String ?? "Project",
                commandLineArgs: value["commandLineArgs"] as? String,
                workingDirectory: value["workingDirectory"] as? String,
                applicationURL: value["applicationUrl"] as? String,
                environment: environment
            )
        }
    }
}

/// Build, rebuild, clean and the command line for starting a project, all through the dotnet CLI.
public struct BuildService: Sendable {
    public let runner: CommandRunner

    public init(runner: CommandRunner) {
        self.runner = runner
    }

    public static func buildArguments(target: String, configuration: String, kind: BuildKind) -> [String] {
        var arguments = [kind == .clean ? "clean" : "build", target, "-c", configuration, "-nologo", "-v:minimal", "-tl:off"]
        if kind == .rebuild {
            arguments.append("--no-incremental")
        }
        return arguments
    }

    /// Arguments for `dotnet run` of an already built project with its launch profile (or none).
    public static func runArguments(project: String, configuration: String, profile: LaunchProfile?) -> [String] {
        var arguments = ["run", "--project", project, "-c", configuration, "--no-build"]
        if let profile {
            arguments += ["--launch-profile", profile.name]
        } else {
            arguments.append("--no-launch-profile")
        }
        return arguments
    }

    public static func workingDirectory(project: String, profile: LaunchProfile?) -> URL {
        let projectDirectory = URL(fileURLWithPath: project).deletingLastPathComponent()
        guard let directory = profile?.workingDirectory, !directory.isEmpty else {
            return projectDirectory
        }
        let expanded = directory.replacingOccurrences(of: "$(ProjectDir)", with: projectDirectory.path + "/", options: .caseInsensitive)
        return URL(fileURLWithPath: expanded, relativeTo: projectDirectory).standardizedFileURL
    }

    public func build(target: String, configuration: String, kind: BuildKind, onOutput: (@Sendable (OutputLine) -> Void)? = nil) async throws -> BuildResult {
        let clock = ContinuousClock()
        let start = clock.now
        let process = try await runner.run(
            Self.buildArguments(target: target, configuration: configuration, kind: kind),
            in: URL(fileURLWithPath: target).deletingLastPathComponent(),
            onOutput: onOutput
        )
        return BuildResult(process: process, diagnostics: BuildOutputParser.parse(process.output), duration: clock.now - start)
    }

    /// The built assembly of a project (`TargetPath`), used by the debugger.
    public func targetPath(project: String, configuration: String) async throws -> String? {
        let result = try await runner.run(["msbuild", project, "-getProperty:TargetPath", "-p:Configuration=\(configuration)", "-nologo"])
        let path = result.standardOutput.trimmingCharacters(in: .whitespacesAndNewlines)
        return result.succeeded && !path.isEmpty ? path : nil
    }
}
