import AppKit
import VsLikeKit

/// Commands of menus, toolbar, palette and context menus.
extension Workspace {
    func show(_ tab: BottomTab) {
        bottomTab = tab
        isBottomPanelVisible = true
    }

    func showTerminal() {
        show(.terminal)
        if terminals.sessions.isEmpty {
            terminals.newShell()
        }
    }

    /// Opens (or creates) `<Solution>.cdiagram.json` next to the solution with all source types.
    func openSolutionDiagram() async {
        guard let solutionURL else { return }
        let url = solutionURL.deletingLastPathComponent().appendingPathComponent(solutionURL.deletingPathExtension().lastPathComponent + DiagramDocument.fileExtension)
        await openDiagram(at: url)
    }

    /// Opens (or creates) a diagram for a folder or project.
    func openDiagram(forFolder folder: URL) async {
        let url = folder.appendingPathComponent(folder.lastPathComponent + DiagramDocument.fileExtension)
        await openDiagram(at: url)
    }

    private func openDiagram(at url: URL) async {
        if !FileManager.default.fileExists(atPath: url.path) {
            guard isEngineReady else {
                status = StatusMessage(text: String(localized: "Wait until the solution is loaded"), style: .warning)
                return
            }
            do {
                try await DiagramEditorModel.create(at: url, workspace: self)
                refreshTree()
            } catch {
                alert = AlertMessage(title: String(localized: "Class Diagram"), message: error.localizedDescription)
                return
            }
        }
        openDiagram(url)
    }

    /// Asks for a file name and creates the file (a class skeleton for `.cs` files) in a folder.
    func promptNewFile(in folder: URL?) {
        let target = folder ?? activeDocument?.url.deletingLastPathComponent() ?? rootURL
        guard let target else { return }
        prompt = PromptRequest(
            title: String(localized: "New File"),
            message: String(localized: "In \(target.lastPathComponent). A name without extension creates a C# class."),
            placeholder: "Customer",
            confirmTitle: String(localized: "Create")
        ) { [weak self] name in
            await self?.createFile(named: name, in: target)
        }
    }

    func createFile(named rawName: String, in folder: URL) async {
        let name = rawName.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return }
        let fileName = name.contains(".") ? name : name + ".cs"
        let url = folder.appendingPathComponent(fileName)
        guard !FileManager.default.fileExists(atPath: url.path) else {
            alert = AlertMessage(title: String(localized: "New File"), message: String(localized: "\(fileName) already exists."))
            return
        }
        var text = ""
        if url.pathExtension == "cs", let engine, isEngineReady {
            let typeName = url.deletingPathExtension().lastPathComponent
            text = (try? await engine.newType(kind: typeName.hasPrefix("I") && typeName.count > 1 && typeName.dropFirst().first!.isUppercase ? .interface : .class, name: typeName, folder: folder.path))?.text ?? ""
        }
        do {
            try text.write(to: url, atomically: true, encoding: .utf8)
            if url.pathExtension == "cs", let engine {
                _ = try? await engine.addDocument(url.path, text: text)
            }
            refreshTree()
            openFile(url)
        } catch {
            alert = AlertMessage(title: String(localized: "New File"), message: error.localizedDescription)
        }
    }

    func promptNewFolder(in folder: URL) {
        prompt = PromptRequest(title: String(localized: "New Folder"), message: folder.lastPathComponent, placeholder: "Models", confirmTitle: String(localized: "Create")) { [weak self] name in
            try? FileManager.default.createDirectory(at: folder.appendingPathComponent(name), withIntermediateDirectories: true)
            self?.refreshTree()
        }
    }

    func promptRename(_ url: URL) {
        prompt = PromptRequest(title: String(localized: "Rename"), message: url.lastPathComponent, placeholder: url.lastPathComponent, initialValue: url.lastPathComponent, confirmTitle: String(localized: "Rename")) { [weak self] name in
            guard let self, !name.isEmpty, name != url.lastPathComponent else { return }
            let target = url.deletingLastPathComponent().appendingPathComponent(name)
            do {
                try FileManager.default.moveItem(at: url, to: target)
                if let document = self.openDocuments.first(where: { $0.url == url }) {
                    document.rename(to: target)
                }
                if url.pathExtension == "cs", let engine = self.engine {
                    _ = try? await engine.removeDocument(url.path)
                    if let text = try? String(contentsOf: target, encoding: .utf8) {
                        _ = try? await engine.addDocument(target.path, text: text)
                    }
                }
                self.refreshTree()
            } catch {
                self.alert = AlertMessage(title: String(localized: "Rename"), message: error.localizedDescription)
            }
        }
    }

    /// Moves a file or folder to the Trash after confirmation.
    func moveToTrash(_ url: URL) async {
        let alert = NSAlert()
        alert.messageText = String(localized: "Move “\(url.lastPathComponent)” to the Trash?")
        alert.addButton(withTitle: String(localized: "Move to Trash"))
        alert.addButton(withTitle: String(localized: "Cancel"))
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            for tab in tabs {
                if case let .code(document) = tab, document.path.hasPrefix(url.path) {
                    tabs.removeAll { $0 == tab }
                }
            }
            if let engine {
                _ = try? await engine.removeDocument(url.path)
            }
            refreshTree()
        } catch {
            self.alert = AlertMessage(title: String(localized: "Move to Trash"), message: error.localizedDescription)
        }
    }

    func revealInFinder(_ url: URL) {
        NSWorkspace.shared.activateFileViewerSelecting([url])
    }

    func removeProjectReference(_ reference: String, from project: String) async {
        do {
            try await ProjectService(runner: try CommandRunner.dotnet()).removeReference(reference, from: project)
            refreshTree()
            await restartEngine()
        } catch {
            alert = AlertMessage(title: String(localized: "Remove Reference"), message: error.localizedDescription)
        }
    }

    func removeProjectFromSolution(_ project: String) async {
        guard let solutionURL else { return }
        do {
            try await ProjectService(runner: try CommandRunner.dotnet()).removeProject(project, from: solutionURL.path)
            refreshTree()
            await restartEngine()
        } catch {
            alert = AlertMessage(title: String(localized: "Remove Project"), message: error.localizedDescription)
        }
    }
}
