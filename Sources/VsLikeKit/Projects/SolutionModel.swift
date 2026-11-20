import Foundation

public enum SolutionFormat: String, Sendable {
    case sln, slnx
}

public struct SolutionProject: Sendable, Hashable, Identifiable {
    public let name: String
    public let filePath: String
    /// Solution folder such as `/src/`, `/` for the root.
    public let solutionFolder: String

    public init(name: String, filePath: String, solutionFolder: String) {
        self.name = name
        self.filePath = filePath
        self.solutionFolder = solutionFolder
    }

    public var id: String { filePath }

    public var directory: URL { URL(fileURLWithPath: filePath).deletingLastPathComponent() }
}

public struct SolutionModel: Sendable, Hashable, Identifiable {
    public var id: String { filePath }

    public let filePath: String
    public let format: SolutionFormat
    public let projects: [SolutionProject]
    public let folders: [String]

    public init(filePath: String, format: SolutionFormat, projects: [SolutionProject], folders: [String]) {
        self.filePath = filePath
        self.format = format
        self.projects = projects
        self.folders = folders
    }

    public var name: String { URL(fileURLWithPath: filePath).deletingPathExtension().lastPathComponent }

    public var directory: URL { URL(fileURLWithPath: filePath).deletingLastPathComponent() }
}

/// Reads `.slnx` (XML) and classic `.sln` files.
public enum SolutionReader {
    static let solutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8"

    public static func read(_ url: URL) throws -> SolutionModel {
        let text = try String(contentsOf: url, encoding: .utf8)
        return url.pathExtension.lowercased() == "slnx" ? try parseSlnx(path: url.path, text: text) : parseSln(path: url.path, text: text)
    }

    public static func parseSlnx(path: String, text: String) throws -> SolutionModel {
        let directory = URL(fileURLWithPath: path).deletingLastPathComponent()
        let document = try XMLDocument(xmlString: text)
        var projects: [SolutionProject] = []
        var folders: [String] = []
        func visit(_ element: XMLElement, folder: String) {
            for child in element.children?.compactMap({ $0 as? XMLElement }) ?? [] {
                if child.name == "Folder", let name = child.attribute(forName: "Name")?.stringValue {
                    folders.append(name)
                    visit(child, folder: name)
                } else if child.name == "Project", let relative = child.attribute(forName: "Path")?.stringValue {
                    let file = directory.appendingPathComponent(relative.replacingOccurrences(of: "\\", with: "/")).standardizedFileURL
                    projects.append(SolutionProject(name: file.deletingPathExtension().lastPathComponent, filePath: file.path, solutionFolder: folder))
                }
            }
        }
        if let root = document.rootElement() {
            visit(root, folder: "/")
        }
        return SolutionModel(filePath: path, format: .slnx, projects: projects, folders: folders)
    }

    public static func parseSln(path: String, text: String) -> SolutionModel {
        let directory = URL(fileURLWithPath: path).deletingLastPathComponent()
        let projectLine = #/^Project\("\{(?<type>[^}]+)\}"\)\s*=\s*"(?<name>[^"]+)",\s*"(?<path>[^"]+)",\s*"\{(?<guid>[^}]+)\}"/#.anchorsMatchLineEndings()
        let nestedLine = #/^\s*\{(?<child>[0-9A-Fa-f-]+)\}\s*=\s*\{(?<parent>[0-9A-Fa-f-]+)\}/#.anchorsMatchLineEndings()
        let entries = text.matches(of: projectLine).map {
            (type: String($0.output.type).uppercased(), name: String($0.output.name), path: String($0.output.path), guid: String($0.output.guid).uppercased())
        }
        var parents: [String: String] = [:]
        for match in text.matches(of: nestedLine) {
            parents[String(match.output.child).uppercased()] = String(match.output.parent).uppercased()
        }
        let folderNames = Dictionary(entries.filter { $0.type == solutionFolderType }.map { ($0.guid, $0.name) }, uniquingKeysWith: { first, _ in first })
        func folderPath(_ guid: String) -> String {
            var segments: [String] = []
            var current = guid
            while let parent = parents[current], let name = folderNames[parent] {
                segments.insert(name, at: 0)
                current = parent
            }
            return segments.isEmpty ? "/" : "/" + segments.joined(separator: "/") + "/"
        }
        let projects = entries
            .filter { $0.type != solutionFolderType && $0.path.lowercased().hasSuffix("proj") }
            .map { entry in
                SolutionProject(
                    name: entry.name,
                    filePath: directory.appendingPathComponent(entry.path.replacingOccurrences(of: "\\", with: "/")).standardizedFileURL.path,
                    solutionFolder: folderPath(entry.guid)
                )
            }
        let folders = folderNames.keys.map { (folderPath($0) + folderNames[$0]! + "/").replacingOccurrences(of: "//", with: "/") }.sorted()
        return SolutionModel(filePath: path, format: .sln, projects: projects, folders: folders)
    }
}
