import AppKit
import Observation
import VsLikeKit

/// A class diagram tab. Types and members always come from the code (read by the engine); every diagram action
/// rewrites only the affected declaration and is one undo step for code and layout together. Positions live in
/// `<Name>.cdiagram.json`.
@MainActor
@Observable
final class DiagramEditorModel: Identifiable {
    enum Tool: Equatable {
        case select
        case create(DiagramTypeKind)
        case inherit
    }

    struct FileChange {
        let path: String
        let before: String?
        let after: String?
    }

    struct UndoEntry {
        let name: String
        let files: [FileChange]
        let layoutBefore: DiagramDocument
        let layoutAfter: DiagramDocument
    }

    let id = UUID()
    let url: URL
    @ObservationIgnored private unowned let workspace: Workspace
    @ObservationIgnored private var refreshTask: Task<Void, Never>?
    @ObservationIgnored private var undoStack: [UndoEntry] = []
    @ObservationIgnored private var redoStack: [UndoEntry] = []

    private(set) var model = DiagramModel.empty
    private(set) var layout: DiagramDocument
    private(set) var isLoading = false
    private(set) var canUndo = false
    private(set) var canRedo = false
    var selection: String?
    var tool: Tool = .select
    var showsExternal = true

    init(url: URL, workspace: Workspace) {
        self.url = url
        self.workspace = workspace
        layout = DiagramDocument.load(url)
    }

    var name: String { DiagramDocument.name(of: url) }

    var folder: URL { url.deletingLastPathComponent() }

    var selectedType: DiagramType? { selection.flatMap(model.find) }

    var boxes: [DiagramBox] {
        let all = DiagramGeometry.boxes(model: model, document: layout)
        return showsExternal ? all : all.filter { !$0.type.isExternal }
    }

    var edges: [DiagramEdge] { DiagramGeometry.edges(model: model, boxes: boxes) }

    /// Source types that are not on the diagram yet (for "Add existing type").
    var missingTypes: [DiagramType] {
        let shown = Set(layout.types.map(\.fullName))
        return model.types.filter { !$0.isExternal && !shown.contains($0.fullName) }.sorted { $0.name < $1.name }
    }

    // MARK: Creating and refreshing

    /// Creates a diagram showing all source types below a folder (and the external types they derive from).
    static func create(at url: URL, workspace: Workspace) async throws {
        guard let engine = workspace.engine, workspace.isEngineReady else {
            throw EngineError.notRunning
        }
        let model = try await engine.diagramModel()
        let prefix = url.deletingLastPathComponent().path + "/"
        var names = Set(model.types.filter { !$0.isExternal && $0.filePaths.contains { $0.hasPrefix(prefix) } }.map(\.fullName))
        for relation in model.relations where names.contains(relation.from) && model.find(relation.to)?.isExternal == true {
            names.insert(relation.to)
        }
        let document = DiagramDocument(types: names.sorted().map { TypePlacement(fullName: $0, x: .nan, y: .nan) })
        try AutoLayout.arrange(document, model: model, all: true).save(to: url)
    }

    func scheduleRefresh(immediately: Bool = false) {
        refreshTask?.cancel()
        refreshTask = Task { [weak self] in
            if !immediately {
                try? await Task.sleep(for: .milliseconds(350))
            }
            guard !Task.isCancelled else { return }
            await self?.refresh()
        }
    }

    func refresh() async {
        guard let engine = workspace.engine, workspace.isEngineReady else {
            return
        }
        isLoading = model.types.isEmpty
        defer { isLoading = false }
        for document in workspace.openDocuments where document.isCSharp {
            workspace.syncNow(document)
        }
        guard let fresh = try? await engine.diagramModel() else {
            return
        }
        model = fresh
        let arranged = AutoLayout.arrange(layout, model: fresh, all: false)
        if arranged != layout {
            saveLayout(arranged)
        }
        if let selection, fresh.find(selection) == nil {
            self.selection = nil
        }
    }

    // MARK: Layout actions

    func move(_ fullName: String, to point: CGPoint) {
        guard let placement = layout.find(fullName) else { return }
        let x = layout.snapToGrid ? BoxMetrics.snap(point.x) : point.x
        let y = layout.snapToGrid ? BoxMetrics.snap(point.y) : point.y
        guard x != placement.x || y != placement.y else { return }
        commit(String(localized: "Move"), files: [], layout: layout.with(placement.moved(x: x, y: y)))
    }

    func toggleCollapsed(_ fullName: String) {
        guard let placement = layout.find(fullName) else { return }
        commit(String(localized: "Collapse"), files: [], layout: layout.with(placement.toggled()))
    }

    func arrangeAll() {
        commit(String(localized: "Arrange"), files: [], layout: AutoLayout.arrange(layout, model: model, all: true))
    }

    func addExisting(_ names: [String]) {
        var all = Set(names)
        for relation in model.relations where all.contains(relation.from) && (relation.kind == .inheritance || relation.kind == .implementation) && model.find(relation.to)?.isExternal == true {
            all.insert(relation.to)
        }
        var next = layout
        for name in all.sorted() where next.find(name) == nil {
            next = next.with(TypePlacement(fullName: name, x: .nan, y: .nan))
        }
        commit(String(localized: "Add Types"), files: [], layout: AutoLayout.arrange(next, model: model, all: false))
    }

    func removeFromDiagram(_ fullName: String) {
        commit(String(localized: "Remove from Diagram"), files: [], layout: layout.without(fullName))
        if selection == fullName { selection = nil }
    }

    func setView(zoom: Double, offset: CGPoint) {
        var next = layout
        next.zoom = zoom
        next.offsetX = offset.x
        next.offsetY = offset.y
        if next != layout { saveLayout(next) }
    }

    func setGrid(show: Bool, snap: Bool) {
        var next = layout
        next.showGrid = show
        next.snapToGrid = snap
        saveLayout(next)
    }

    // MARK: Code actions

    /// Creates `<Name>.cs` in the diagram's folder with the namespace from RootNamespace plus the folder.
    func createType(_ kind: DiagramTypeKind, name: String, at point: CGPoint) async {
        guard let engine = workspace.engine else { return }
        do {
            let created = try await engine.newType(kind: kind, name: name, folder: folder.path)
            if FileManager.default.fileExists(atPath: created.path) {
                workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: String(localized: "\(URL(fileURLWithPath: created.path).lastPathComponent) already exists."))
                return
            }
            let placement = TypePlacement(fullName: created.fullName, x: BoxMetrics.snap(point.x), y: BoxMetrics.snap(point.y))
            await change(String(localized: "Create \(name)"), files: [created.path: created.text], layout: layout.with(placement))
            selection = created.fullName
        } catch {
            workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: error.localizedDescription)
        }
    }

    func addMember(to type: DiagramType, kind: MemberKind, text: String) async {
        guard let engine = workspace.engine else { return }
        guard let member = try? await engine.parseMember(kind: kind, text: text, owner: type.name) else {
            workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: String(localized: "“\(text)” is not a valid member. Example: + Total(withTax: bool): decimal"))
            return
        }
        await editTypeFile(type, actionName: String(localized: "Add \(member.name)"), signature: nil) { source in
            try await engine.addMember(source: source, type: type.fullName, member: member)
        }
    }

    func updateMember(_ old: DiagramMember, in type: DiagramType, text: String) async {
        guard let engine = workspace.engine else { return }
        guard let parsed = try? await engine.parseMember(kind: old.kind, text: text, owner: type.name) else {
            workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: String(localized: "“\(text)” is not a valid member."))
            return
        }
        let member = DiagramMember(kind: parsed.kind, name: parsed.name, type: parsed.type, access: parsed.access, modifiers: old.modifiers, accessors: old.accessors, parameters: parsed.parameters, value: parsed.value)
        await editTypeFile(type, actionName: String(localized: "Change \(member.name)"), signature: old.signature) { source in
            try await engine.updateMember(source: source, type: type.fullName, signature: old.signature, member: member)
        }
    }

    func removeMember(_ member: DiagramMember, from type: DiagramType) async {
        guard let engine = workspace.engine else { return }
        await editTypeFile(type, actionName: String(localized: "Delete \(member.name)"), signature: member.signature) { source in
            try await engine.removeMember(source: source, type: type.fullName, signature: member.signature)
        }
    }

    func setModifiers(_ type: DiagramType, access: Access, modifiers: TypeModifiers) async {
        guard let engine = workspace.engine else { return }
        await editTypeFile(type, actionName: String(localized: "Change Modifiers"), signature: nil) { source in
            try await engine.setModifiers(source: source, type: type.fullName, access: access, modifiers: modifiers)
        }
    }

    func setBaseList(_ type: DiagramType, baseType: String?, interfaces: [String]) async {
        guard let engine = workspace.engine else { return }
        await editTypeFile(type, actionName: String(localized: "Change Base Types"), signature: nil, baseList: true) { source in
            try await engine.setBaseList(source: source, type: type.fullName, baseType: baseType, interfaces: interfaces)
        }
    }

    /// A drawn connection: base class (same kind, not sealed or static) or interface implementation.
    func connect(from: String, to: String) async {
        guard let source = model.find(from), !source.isExternal, let target = model.find(to), from != to else { return }
        let targetName = target.displayName
        if target.kind == .interface {
            if !source.interfaces.contains(targetName) {
                await setBaseList(source, baseType: source.baseType, interfaces: source.interfaces + [targetName])
            }
        } else if (source.kind == .class || source.kind == .record) && target.kind == source.kind
            && !target.modifiers.contains(.sealed) && !target.modifiers.contains(.static) {
            await setBaseList(source, baseType: targetName, interfaces: source.interfaces)
        } else {
            workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: String(localized: "\(source.name) cannot derive from \(target.name)."))
        }
    }

    /// Deletes a type from the code after confirmation (the whole file when it held only this type).
    func deleteType(_ type: DiagramType) async {
        guard let engine = workspace.engine, !type.isExternal, let file = type.filePaths.first else {
            removeFromDiagram(type.fullName)
            return
        }
        let source = read(file)
        let offset = (try? await engine.locate(source: source, type: type.fullName, signature: nil))?.identifierOffset ?? -1
        let references = offset >= 0 ? max(0, ((try? await engine.references(file, offset: offset))?.count ?? 0) - type.filePaths.count) : 0
        let alert = NSAlert()
        alert.messageText = String(localized: "Delete \(type.name)?")
        alert.informativeText = references == 0
            ? String(localized: "The type is removed from the code.")
            : String(localized: "The type is removed from the code. \(references) references to it will no longer compile.")
        alert.addButton(withTitle: String(localized: "Delete"))
        alert.addButton(withTitle: String(localized: "Cancel"))
        alert.buttons.first?.hasDestructiveAction = true
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        var changes: [String: String?] = [:]
        for path in type.filePaths {
            changes[path] = try? await engine.removeType(source: read(path), type: type.fullName)
        }
        await change(String(localized: "Delete \(type.name)"), files: changes, layout: layout.without(type.fullName))
        selection = nil
    }

    /// Renames a type with Roslyn (all references) and its file when the file is named after the type.
    func renameType(_ type: DiagramType, to newName: String) async {
        guard let engine = workspace.engine, !type.isExternal, let file = type.filePaths.first, newName != type.name, !newName.isEmpty else { return }
        do {
            let located = try await engine.locate(source: read(file), type: type.fullName, signature: nil)
            let edit = try await engine.rename(file, offset: located.identifierOffset, newName: newName)
            var changes: [String: String?] = [:]
            for fileEdit in edit.files {
                changes[fileEdit.filePath] = TextEditing.apply(fileEdit.edits, to: read(fileEdit.filePath))
            }
            if URL(fileURLWithPath: file).deletingPathExtension().lastPathComponent == type.name, let renamed = changes[file] ?? read(file) {
                let target = URL(fileURLWithPath: file).deletingLastPathComponent().appendingPathComponent(newName + ".cs").path
                if !FileManager.default.fileExists(atPath: target) {
                    changes[file] = .some(nil)
                    changes[target] = renamed
                }
            }
            let arity = type.typeParameters.isEmpty ? "" : "`\(type.typeParameters.count)"
            let newFullName = String(type.fullName.dropLast(type.name.count + arity.count)) + newName + arity
            await change(String(localized: "Rename \(type.name)"), files: changes, layout: layout.renamed(type.fullName, to: newFullName))
            selection = newFullName
        } catch {
            workspace.alert = AlertMessage(title: String(localized: "Rename"), message: error.localizedDescription)
        }
    }

    func openInEditor(_ type: DiagramType, member: DiagramMember? = nil) async {
        guard let engine = workspace.engine else { return }
        for file in type.filePaths {
            guard let located = try? await engine.locate(source: read(file), type: type.fullName, signature: member?.signature), located.line > 0 else {
                continue
            }
            workspace.openFile(URL(fileURLWithPath: file), line: located.line)
            return
        }
    }

    func exportSVG() -> String {
        SVGExporter.export(model: model, document: layout)
    }

    // MARK: Undo

    func undo() async {
        guard let entry = undoStack.popLast() else { return }
        await write(entry.files.map { ($0.path, $0.before) })
        saveLayout(entry.layoutBefore)
        redoStack.append(entry)
        updateUndoState()
        await refresh()
    }

    func redo() async {
        guard let entry = redoStack.popLast() else { return }
        await write(entry.files.map { ($0.path, $0.after) })
        saveLayout(entry.layoutAfter)
        undoStack.append(entry)
        updateUndoState()
        await refresh()
    }

    var undoName: String? { undoStack.last?.name }

    var redoName: String? { redoStack.last?.name }

    // MARK: Private

    /// Runs a writer on the file that declares the type (for partial types: the part with the member or base list).
    private func editTypeFile(_ type: DiagramType, actionName: String, signature: String?, baseList: Bool = false, edit: (String) async throws -> String?) async {
        guard let engine = workspace.engine, !type.isExternal else { return }
        var target: String?
        for file in type.filePaths {
            guard let located = try? await engine.locate(source: read(file), type: type.fullName, signature: signature), located.identifierOffset >= 0 else {
                continue
            }
            if signature != nil && located.line == 0 {
                continue
            }
            if baseList && type.filePaths.count > 1 && !read(file).contains(":") && file != type.filePaths.last {
                continue
            }
            target = file
            break
        }
        guard let target else { return }
        do {
            let updated = try await edit(read(target))
            await change(actionName, files: [target: updated], layout: layout)
        } catch {
            workspace.alert = AlertMessage(title: String(localized: "Class Diagram"), message: error.localizedDescription)
        }
    }

    private func change(_ name: String, files: [String: String?], layout next: DiagramDocument) async {
        var changes: [FileChange] = []
        for (path, after) in files {
            let exists = FileManager.default.fileExists(atPath: path) || workspace.openDocuments.contains { $0.path == path }
            let before = exists ? read(path) : nil
            if before != after {
                changes.append(FileChange(path: path, before: before, after: after))
            }
        }
        await write(changes.map { ($0.path, $0.after) })
        commit(name, files: changes, layout: next)
        await refresh()
    }

    private func commit(_ name: String, files: [FileChange], layout next: DiagramDocument) {
        guard !files.isEmpty || next != layout else { return }
        undoStack.append(UndoEntry(name: name, files: files, layoutBefore: layout, layoutAfter: next))
        redoStack.removeAll()
        saveLayout(next)
        updateUndoState()
    }

    private func updateUndoState() {
        canUndo = !undoStack.isEmpty
        canRedo = !redoStack.isEmpty
    }

    private func saveLayout(_ next: DiagramDocument) {
        layout = next
        try? next.save(to: url)
    }

    /// Writes file contents (nil deletes). Open documents are edited in place so their tab and undo stay valid.
    private func write(_ files: [(String, String?)]) async {
        for (path, text) in files {
            let document = workspace.openDocuments.first { $0.path == path }
            if let text {
                if let document {
                    let edit = TextEditing.minimalEdit(from: document.text, to: text)
                    document.apply([edit], actionName: String(localized: "Class Diagram"))
                    workspace.save(document)
                } else {
                    let existed = FileManager.default.fileExists(atPath: path)
                    try? FileManager.default.createDirectory(at: URL(fileURLWithPath: path).deletingLastPathComponent(), withIntermediateDirectories: true)
                    try? text.write(toFile: path, atomically: true, encoding: .utf8)
                    if let engine = workspace.engine {
                        if existed {
                            engine.updateDocument(path, text: text)
                        } else {
                            _ = try? await engine.addDocument(path, text: text)
                        }
                    }
                }
            } else {
                if let document, let tab = workspace.tabs.first(where: { $0.id == document.id }) {
                    try? document.save()
                    workspace.close(tab)
                }
                try? FileManager.default.removeItem(atPath: path)
                _ = try? await workspace.engine?.removeDocument(path)
            }
        }
        workspace.refreshTree()
    }

    private func read(_ path: String) -> String {
        workspace.openDocuments.first { $0.path == path }?.text ?? (try? String(contentsOfFile: path, encoding: .utf8)) ?? ""
    }
}
