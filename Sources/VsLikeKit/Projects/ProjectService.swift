import Foundation

public struct ProjectTemplate: Sendable, Hashable, Identifiable {
    public init(name: String, shortName: String, tags: String) {
        self.name = name
        self.shortName = shortName
        self.tags = tags
    }

    public let name: String
    public let shortName: String
    public let tags: String

    public var id: String { shortName }
}

public struct InstalledPackage: Sendable, Hashable, Identifiable {
    public let id: String
    public let requestedVersion: String?
    public let resolvedVersion: String?
    public let latestVersion: String?

    public var hasUpdate: Bool { latestVersion != nil && resolvedVersion != nil && latestVersion != resolvedVersion }
}

/// Solutions, projects, references and packages through the dotnet CLI (`dotnet new`, `sln`, `add`, `remove`, `list`).
public struct ProjectService: Sendable {
    public static let preferredTemplates = ["console", "classlib", "xunit", "nunit", "mstest", "worker", "webapi", "web", "blazor"]
    /// Exit code of `dotnet new` for an option the template does not know (aka.ms/templating-exit-codes#127).
    static let invalidOptionExitCode: Int32 = 127
    static let windowsOnly: Set<String> = ["winforms", "winformslib", "winformscontrollib", "wpf", "wpflib", "wpfcustomcontrollib", "wpfusercontrollib"]

    public let runner: CommandRunner

    public init(runner: CommandRunner) {
        self.runner = runner
    }

    public func createSolution(in directory: URL, name: String, format: SolutionFormat = .slnx) async throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try await runner.runChecked(["new", "sln", "--name", name, "--output", directory.path, "--format", format.rawValue], in: directory)
        return directory.appendingPathComponent(name + "." + format.rawValue)
    }

    public func createProject(template: String, name: String, in directory: URL, framework: String? = nil) async throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let output = directory.appendingPathComponent(name)
        var arguments = ["new", template, "--name", name, "--output", output.path]
        if let framework, !framework.isEmpty {
            arguments += ["--framework", framework]
        }
        // A Program class with Main in the project's namespace instead of top-level statements. Templates without the
        // option end with exit code 127 ("invalid option") before creating anything and are created without it.
        let withMain = try await runner.run(arguments + ["--use-program-main"], in: directory)
        if withMain.exitCode == Self.invalidOptionExitCode && !withMain.wasCancelled {
            try await runner.runChecked(arguments, in: directory)
        } else if !withMain.succeeded {
            throw CommandError.failed(command: "dotnet " + arguments.joined(separator: " "), exitCode: withMain.exitCode, output: withMain.allText)
        }
        let project = output.appendingPathComponent(name + ".csproj")
        guard FileManager.default.fileExists(atPath: project.path) else {
            throw CommandError.failed(command: "dotnet new \(template)", exitCode: 0, output: "No project file was created at \(project.path).")
        }
        try useBlockScopedNamespaces(in: output)
        return project
    }

    /// The templates write `namespace X;`; VsLike's new files use `namespace X { … }`.
    private func useBlockScopedNamespaces(in folder: URL) throws {
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        for file in files where file.pathExtension == "cs" {
            let source = try String(contentsOf: file, encoding: .utf8)
            let converted = NamespaceStyle.blockScoped(source)
            if converted != source {
                try converted.write(to: file, atomically: true, encoding: .utf8)
            }
        }
    }

    public func addProject(_ project: String, to solution: String, folder: String? = nil) async throws {
        var arguments = ["sln", solution, "add", project]
        if let folder, folder != "/" {
            arguments += ["--solution-folder", folder.trimmingCharacters(in: CharacterSet(charactersIn: "/"))]
        }
        try await runner.runChecked(arguments, in: URL(fileURLWithPath: solution).deletingLastPathComponent())
    }

    public func removeProject(_ project: String, from solution: String) async throws {
        try await runner.runChecked(["sln", solution, "remove", project], in: URL(fileURLWithPath: solution).deletingLastPathComponent())
    }

    public func addReference(_ reference: String, to project: String) async throws {
        try await runner.runChecked(["add", project, "reference", reference])
    }

    public func removeReference(_ reference: String, from project: String) async throws {
        try await runner.runChecked(["remove", project, "reference", reference])
    }

    public func addPackage(_ id: String, version: String?, to project: String) async throws {
        var arguments = ["add", project, "package", id]
        if let version, !version.isEmpty {
            arguments += ["--version", version]
        }
        try await runner.runChecked(arguments)
    }

    public func removePackage(_ id: String, from project: String) async throws {
        try await runner.runChecked(["remove", project, "package", id])
    }

    public func listPackages(of project: String, outdated: Bool = false) async throws -> [InstalledPackage] {
        var arguments = ["list", project, "package", "--format", "json"]
        if outdated {
            arguments.append("--outdated")
        }
        let result = try await runner.runChecked(arguments)
        return Self.parsePackageList(result.standardOutput)
    }

    public func templates() async -> [ProjectTemplate] {
        guard let result = try? await runner.run(["new", "list", "--type", "project", "--language", "C#"]), result.succeeded else {
            return []
        }
        return Self.parseTemplates(result.standardOutput)
    }

    public static func parsePackageList(_ json: String) -> [InstalledPackage] {
        guard let start = json.firstIndex(of: "{"),
              let root = try? JSONSerialization.jsonObject(with: Data(json[start...].utf8)) as? [String: Any] else {
            return []
        }
        var seen = Set<String>()
        var packages: [InstalledPackage] = []
        for project in root["projects"] as? [[String: Any]] ?? [] {
            for framework in project["frameworks"] as? [[String: Any]] ?? [] {
                for package in framework["topLevelPackages"] as? [[String: Any]] ?? [] {
                    guard let id = package["id"] as? String, seen.insert(id).inserted else {
                        continue
                    }
                    packages.append(InstalledPackage(
                        id: id,
                        requestedVersion: package["requestedVersion"] as? String,
                        resolvedVersion: package["resolvedVersion"] as? String,
                        latestVersion: package["latestVersion"] as? String
                    ))
                }
            }
        }
        return packages
    }

    /// Parses the table of `dotnet new list` (columns are found from the dashed ruler line).
    public static func parseTemplates(_ output: String) -> [ProjectTemplate] {
        let lines = output.components(separatedBy: "\n").map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "\r")) }
        guard let rulerIndex = lines.firstIndex(where: { $0.hasPrefix("---") && $0.contains("  ") }) else {
            return []
        }
        var columns: [Range<Int>] = []
        var start: Int?
        for (index, character) in Array(lines[rulerIndex] + " ").enumerated() {
            if character == "-", start == nil {
                start = index
            } else if character != "-", let begin = start {
                columns.append(begin..<index)
                start = nil
            }
        }
        guard columns.count >= 3 else {
            return []
        }
        columns[columns.count - 1] = columns[columns.count - 1].lowerBound..<Int.max
        var templates: [ProjectTemplate] = []
        for line in lines[(rulerIndex + 1)...] where !line.trimmingCharacters(in: .whitespaces).isEmpty {
            let characters = Array(line)
            func cell(_ index: Int) -> String {
                let range = columns[index]
                guard range.lowerBound < characters.count else {
                    return ""
                }
                return String(characters[range.lowerBound..<min(characters.count, range.upperBound)]).trimmingCharacters(in: .whitespaces)
            }
            let shortName = cell(1).split(separator: ",").first.map { $0.trimmingCharacters(in: .whitespaces) } ?? ""
            if shortName.isEmpty || windowsOnly.contains(shortName.lowercased()) {
                continue
            }
            templates.append(ProjectTemplate(name: cell(0), shortName: shortName, tags: columns.count > 3 ? cell(3) : ""))
        }
        return templates.sorted { lhs, rhs in
            let l = preferredTemplates.firstIndex(of: lhs.shortName) ?? Int.max
            let r = preferredTemplates.firstIndex(of: rhs.shortName) ?? Int.max
            return l != r ? l < r : lhs.name.localizedCaseInsensitiveCompare(rhs.name) == .orderedAscending
        }
    }
}
