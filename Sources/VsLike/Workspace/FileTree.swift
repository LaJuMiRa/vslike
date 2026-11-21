import CoreServices
import Foundation
import VsLikeKit

/// A node of the solution tree: solution folders, projects, folders and files.
struct FileNode: Identifiable, Hashable {
    enum Kind: Hashable {
        case solutionFolder
        case project(isTest: Bool)
        case folder
        case file
        case dependencies
        case reference(path: String)
        case package(id: String, version: String?)
    }

    let id: String
    let name: String
    let url: URL?
    let kind: Kind
    var children: [FileNode]?

    var symbol: String {
        switch kind {
        case .solutionFolder: return "folder.fill.badge.gearshape"
        case let .project(isTest): return isTest ? "checklist" : "shippingbox.fill"
        case .folder: return "folder.fill"
        case .dependencies: return "link"
        case .reference: return "arrow.triangle.branch"
        case .package: return "cube.box"
        case .file: return FileNode.symbol(forFile: name)
        }
    }

    static func symbol(forFile name: String) -> String {
        if DiagramDocument.isDiagram(URL(fileURLWithPath: name)) {
            return "rectangle.3.group"
        }
        switch (name as NSString).pathExtension.lowercased() {
        case "cs": return "c.square"
        case "csproj", "slnx", "sln", "props", "targets": return "gearshape"
        case "json": return "curlybraces"
        case "md": return "doc.richtext"
        case "xml", "config", "resx", "xaml", "axaml": return "chevron.left.forwardslash.chevron.right"
        case "png", "jpg", "jpeg", "svg", "ico": return "photo"
        default: return "doc.text"
        }
    }
}

/// Builds the tree shown in the sidebar from the solution (or a plain folder) and the file system.
enum FileTreeBuilder {
    static let ignored: Set<String> = ["bin", "obj", ".git", ".vs", ".vslike", ".idea", "node_modules", "artifacts", ".DS_Store", "TestResults"]

    static func solutionTree(_ solution: SolutionModel) -> [FileNode] {
        var folders: [String: [FileNode]] = [:]
        for project in solution.projects {
            folders[project.solutionFolder, default: []].append(projectNode(project))
        }
        func build(_ path: String) -> [FileNode] {
            let childFolders = Set(solution.folders + folders.keys).filter { folder in
                folder != path && folder.hasPrefix(path) && !folder.dropFirst(path.count).dropLast().contains("/")
            }
            let folderNodes = childFolders.sorted().map { folder in
                FileNode(id: "sf:" + folder, name: String(folder.dropFirst(path.count).dropLast()), url: nil, kind: .solutionFolder, children: build(folder))
            }
            return folderNodes + (folders[path] ?? []).sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        }
        let topLevelFiles = files(in: solution.directory, depth: 0).filter {
            if case .file = $0.kind { return true }
            return false
        }
        return build("/") + topLevelFiles.filter { !$0.name.hasSuffix(".sln") && !$0.name.hasSuffix(".slnx") || $0.url?.path == solution.filePath }
    }

    static func projectNode(_ project: SolutionProject) -> FileNode {
        let file = (try? ProjectFile.load(project.filePath))
        var dependencies: [FileNode] = []
        for reference in file?.projectReferences() ?? [] {
            let name = URL(fileURLWithPath: reference).deletingPathExtension().lastPathComponent
            dependencies.append(FileNode(id: "ref:\(project.filePath)>\(reference)", name: name, url: URL(fileURLWithPath: reference), kind: .reference(path: reference)))
        }
        for package in file?.packageReferences() ?? [] {
            let label = package.version.map { "\(package.id) \($0)" } ?? package.id
            dependencies.append(FileNode(id: "pkg:\(project.filePath)>\(package.id)", name: label, url: nil, kind: .package(id: package.id, version: package.version)))
        }
        var children = files(in: project.directory, depth: 0)
        children.insert(FileNode(id: "deps:" + project.filePath, name: String(localized: "Dependencies"), url: nil, kind: .dependencies, children: dependencies), at: 0)
        return FileNode(id: "proj:" + project.filePath, name: project.name, url: URL(fileURLWithPath: project.filePath), kind: .project(isTest: file?.isTestProject ?? false), children: children)
    }

    /// Folders first, then files, both in Finder order; build output and hidden folders are skipped.
    static func files(in directory: URL, depth: Int) -> [FileNode] {
        guard depth < 12, let names = try? FileManager.default.contentsOfDirectory(atPath: directory.path) else {
            return []
        }
        var folders: [FileNode] = []
        var files: [FileNode] = []
        for name in names where !ignored.contains(name) && !name.hasPrefix(".") {
            let url = directory.appendingPathComponent(name)
            var isDirectory: ObjCBool = false
            FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory)
            if isDirectory.boolValue {
                folders.append(FileNode(id: url.path, name: name, url: url, kind: .folder, children: self.files(in: url, depth: depth + 1)))
            } else if !name.hasSuffix(".csproj") {
                files.append(FileNode(id: url.path, name: name, url: url, kind: .file))
            }
        }
        let order: (FileNode, FileNode) -> Bool = { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        return folders.sorted(by: order) + files.sorted(by: order)
    }

    /// All files below the root (for quick open), relative path and URL.
    static func allFiles(_ nodes: [FileNode]) -> [FileNode] {
        nodes.flatMap { node -> [FileNode] in
            if case .file = node.kind { return [node] }
            if case .dependencies = node.kind { return [] }
            return allFiles(node.children ?? [])
        }
    }
}

/// Watches a folder tree with FSEvents and reports changed paths (coalesced, on the main queue).
final class FolderWatcher: @unchecked Sendable {
    private var stream: FSEventStreamRef?
    private let handler: @MainActor ([String]) -> Void

    init(url: URL, handler: @escaping @MainActor ([String]) -> Void) {
        self.handler = handler
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, info, count, paths, _, _ in
            guard let info else { return }
            let watcher = Unmanaged<FolderWatcher>.fromOpaque(info).takeUnretainedValue()
            let array = unsafeBitCast(paths, to: NSArray.self) as? [String] ?? []
            let changed = Array(array.prefix(count))
            DispatchQueue.main.async {
                MainActor.assumeIsolated { watcher.handler(changed) }
            }
        }
        let flags = UInt32(kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer)
        stream = FSEventStreamCreate(nil, callback, &context, [url.path] as CFArray, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.3, flags)
        if let stream {
            FSEventStreamSetDispatchQueue(stream, DispatchQueue.global(qos: .utility))
            FSEventStreamStart(stream)
        }
    }

    func stop() {
        guard let stream else { return }
        FSEventStreamStop(stream)
        FSEventStreamInvalidate(stream)
        FSEventStreamRelease(stream)
        self.stream = nil
    }

    deinit {
        stop()
    }
}
