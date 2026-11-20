import AppKit
import VsLikeKit

/// Applying engine edits across files, navigation, rename and formatting.
extension Workspace {
    /// Applies an edit from a code fix, rename, completion or formatting. Open documents change undoably; closed
    /// files are written to disk and pushed to the engine; new files are created and opened.
    func apply(_ edit: WorkspaceEdit, actionName: String) async {
        for file in edit.files {
            let path = RealPath.resolve(file.filePath)
            if let text = file.newFileText {
                let url = URL(fileURLWithPath: file.filePath)
                do {
                    try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try text.write(to: url, atomically: true, encoding: .utf8)
                    if let engine, isEngineReady {
                        _ = try? await engine.addDocument(url.path, text: text)
                    }
                    openFile(url)
                } catch {
                    alert = AlertMessage(title: actionName, message: error.localizedDescription)
                }
            } else if let document = openDocuments.first(where: { $0.path == path }) {
                document.apply(file.edits, actionName: actionName)
            } else if let current = try? String(contentsOfFile: path, encoding: .utf8) {
                let updated = TextEditing.apply(file.edits, to: current)
                do {
                    try updated.write(toFile: path, atomically: true, encoding: .utf8)
                    engine?.updateDocument(path, text: updated)
                } catch {
                    alert = AlertMessage(title: actionName, message: error.localizedDescription)
                }
            }
        }
        if let caretFile = edit.caretFile, let offset = edit.caretOffset, let document = openFile(URL(fileURLWithPath: caretFile)) {
            document.pendingSelection = NSRange(location: offset, length: 0)
        }
        refreshTree()
        notifyCodeChanged()
    }

    func navigate(to location: SourceLocation) {
        openFile(URL(fileURLWithPath: location.filePath), line: location.line, column: location.column, selectLength: location.length)
    }

    func navigate(to diagnostic: Diagnostic) {
        guard let file = diagnostic.filePath else {
            return
        }
        openFile(URL(fileURLWithPath: file), line: diagnostic.startLine, column: diagnostic.startColumn, selectLength: diagnostic.length)
    }

    func goToDefinition(_ document: CodeDocument, offset: Int) async {
        guard let engine, isEngineReady else { return }
        syncNow(document)
        do {
            let locations = try await engine.definition(document.path, offset: offset)
            if locations.count == 1 {
                navigate(to: locations[0])
            } else if locations.count > 1 {
                references = ReferenceList(title: String(localized: "Definitions"), locations: locations)
            } else {
                status = StatusMessage(text: String(localized: "No definition found"), style: .warning)
            }
        } catch {
            status = StatusMessage(text: error.localizedDescription, style: .error)
        }
    }

    func findReferences(_ document: CodeDocument, offset: Int) async {
        guard let engine, isEngineReady else { return }
        syncNow(document)
        do {
            let locations = try await engine.references(document.path, offset: offset)
            references = ReferenceList(title: String(localized: "\(locations.count) references"), locations: locations)
        } catch {
            status = StatusMessage(text: error.localizedDescription, style: .error)
        }
    }

    func rename(_ document: CodeDocument, offset: Int) async {
        guard let engine, isEngineReady else { return }
        syncNow(document)
        guard let target = try? await engine.prepareRename(document.path, offset: offset) else {
            status = StatusMessage(text: String(localized: "Nothing to rename here"), style: .warning)
            return
        }
        guard target.canRename else {
            alert = AlertMessage(title: String(localized: "Rename"), message: target.reason ?? String(localized: "This symbol cannot be renamed."))
            return
        }
        prompt = PromptRequest(
            title: String(localized: "Rename \(target.symbolDisplay)"),
            message: String(localized: "\(target.referenceCount) references will be updated."),
            placeholder: target.currentName,
            initialValue: target.currentName,
            confirmTitle: String(localized: "Rename")
        ) { [weak self] newName in
            guard let self, newName != target.currentName, !newName.isEmpty else { return }
            do {
                let edit = try await engine.rename(document.path, offset: offset, newName: newName)
                await self.apply(edit, actionName: String(localized: "Rename"))
            } catch {
                self.alert = AlertMessage(title: String(localized: "Rename"), message: error.localizedDescription)
            }
        }
    }

    func formatDocument(_ document: CodeDocument) async {
        guard let engine, isEngineReady, document.isCSharp else { return }
        syncNow(document)
        if let edit = try? await engine.format(document.path) {
            await apply(edit, actionName: String(localized: "Format Document"))
        }
    }

    /// Code actions at a range: fixes for diagnostics there (StyleCop, compiler) and refactorings (generate method,
    /// add using, extract …).
    func codeActions(_ document: CodeDocument, range: NSRange) async -> [CodeAction] {
        guard let engine, isEngineReady, document.isCSharp else { return [] }
        syncNow(document)
        return (try? await engine.codeActions(document.path, start: range.location, length: range.length)) ?? []
    }

    func perform(_ action: CodeAction) async {
        guard let engine else { return }
        do {
            let edit = try await engine.resolveCodeAction(action.handle)
            if edit.isEmpty {
                status = StatusMessage(text: String(localized: "The action made no changes"), style: .warning)
            } else {
                await apply(edit, actionName: action.title)
                status = StatusMessage(text: action.title, style: .success)
            }
        } catch {
            alert = AlertMessage(title: action.title, message: error.localizedDescription)
        }
    }

    /// Applies every StyleCop "fix all in document" action for the document's rule violations, rule by rule with
    /// fresh diagnostics after each fix (offsets move).
    func fixAllStyleCop(in document: CodeDocument) async {
        guard let engine, isEngineReady else { return }
        var handled = Set<String>()
        var fixed = 0
        while true {
            syncNow(document)
            let current = (try? await engine.diagnostics(document.path)) ?? []
            diagnostics.setLive(current, for: document.path)
            guard let next = current.first(where: { $0.isStyleCop && !handled.contains($0.code) }) else {
                break
            }
            handled.insert(next.code)
            let actions = await codeActions(document, range: NSRange(location: next.start, length: max(next.length, 0)))
            let fixAll = actions.first { $0.group == next.code && $0.isFixAll && ($0.title.contains("document") || $0.title.contains("Dokument")) }
            if let action = fixAll ?? actions.first(where: { $0.group == next.code && $0.isFix }) {
                await perform(action)
                fixed += 1
            }
        }
        status = fixed == 0
            ? StatusMessage(text: String(localized: "No StyleCop issues to fix in this file"), style: .success)
            : StatusMessage(text: String(localized: "Fixed \(fixed) StyleCop rules"), style: .success)
    }
}
