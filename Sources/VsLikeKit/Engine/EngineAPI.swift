import Foundation

/// Typed calls of the engine protocol. Paths are absolute file paths.
extension EngineClient {
    public func initialize(culture: String?) async throws -> EngineInfo {
        try await request("initialize", ["culture": JSONValue.optional(culture)] as JSONValue, timeout: 60)
    }

    public func load(_ path: String) async throws -> WorkspaceLoadResult {
        try await request("workspace/load", ["path": .string(path)] as JSONValue)
    }

    public func projects() async throws -> [WorkspaceProject] {
        try await request("workspace/projects", JSONValue.null)
    }

    /// Pushes unsaved editor text; diagnostics follow as `workspace/diagnostics` notifications.
    public func updateDocument(_ path: String, text: String) {
        notify("document/update", ["path": .string(path), "text": .string(text)])
    }

    public func addDocument(_ path: String, text: String) async throws -> Bool {
        try await request("document/add", ["path": .string(path), "text": .string(text)] as JSONValue)
    }

    public func removeDocument(_ path: String) async throws -> Bool {
        try await request("document/remove", ["path": .string(path)] as JSONValue)
    }

    public func diagnostics(_ path: String) async throws -> [Diagnostic] {
        try await request("document/diagnostics", ["path": .string(path)] as JSONValue)
    }

    public func refreshAllDiagnostics() async throws {
        _ = try await rawRequest("workspace/refreshDiagnostics", params: .null)
    }

    public func classify(_ path: String, start: Int, length: Int) async throws -> [ClassifiedSpan] {
        try await request("document/classify", ["path": .string(path), "start": .int(start), "length": .int(length)] as JSONValue)
    }

    public func quickInfo(_ path: String, offset: Int) async throws -> QuickInfo? {
        try await request("document/quickInfo", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    /// Loads or unloads StyleCop (downloads the pinned package into the NuGet cache when needed).
    public func setStyleCop(_ enabled: Bool) async throws -> Bool {
        try await request("styleCop/set", ["enabled": .bool(enabled)] as JSONValue, timeout: 300)
    }

    public func shouldTriggerCompletion(_ path: String, offset: Int, typed: Character) async throws -> Bool {
        try await request("completion/shouldTrigger", ["path": .string(path), "offset": .int(offset), "typed": .string(String(typed))] as JSONValue)
    }

    public func completion(_ path: String, offset: Int) async throws -> CompletionList? {
        try await request("completion/list", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func completionDescription(_ path: String, handle: Int) async throws -> String? {
        try await request("completion/description", ["path": .string(path), "handle": .int(handle)] as JSONValue)
    }

    public func resolveCompletion(_ path: String, handle: Int) async throws -> WorkspaceEdit {
        try await request("completion/resolve", ["path": .string(path), "handle": .int(handle)] as JSONValue)
    }

    public func signatureHelp(_ path: String, offset: Int) async throws -> SignatureHelp? {
        try await request("signatureHelp", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func codeActions(_ path: String, start: Int, length: Int) async throws -> [CodeAction] {
        try await request("codeActions/list", ["path": .string(path), "start": .int(start), "length": .int(length)] as JSONValue)
    }

    public func resolveCodeAction(_ handle: Int) async throws -> WorkspaceEdit {
        try await request("codeActions/resolve", ["handle": .int(handle)] as JSONValue)
    }

    public func format(_ path: String) async throws -> WorkspaceEdit {
        try await request("document/format", ["path": .string(path)] as JSONValue)
    }

    public func definition(_ path: String, offset: Int) async throws -> [SourceLocation] {
        try await request("nav/definition", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func references(_ path: String, offset: Int) async throws -> [SourceLocation] {
        try await request("nav/references", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func declaration(typeName: String, memberName: String?) async throws -> SourceLocation? {
        try await request("nav/declaration", ["typeName": .string(typeName), "memberName": .optional(memberName)] as JSONValue)
    }

    public func enclosingType(_ path: String, offset: Int) async throws -> String? {
        try await request("document/enclosingType", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func prepareRename(_ path: String, offset: Int) async throws -> RenameTarget? {
        try await request("rename/prepare", ["path": .string(path), "offset": .int(offset)] as JSONValue)
    }

    public func rename(_ path: String, offset: Int, newName: String) async throws -> WorkspaceEdit {
        try await request("rename/apply", ["path": .string(path), "offset": .int(offset), "newName": .string(newName)] as JSONValue)
    }
}

/// Class diagram calls. Writers take the current source and return the changed source (nil deletes the file).
extension EngineClient {
    struct SourceResult: Decodable {
        let text: String?
    }

    public struct LocateResult: Decodable, Sendable {
        public let line: Int
        public let identifierOffset: Int
    }

    public struct NewTypeResult: Decodable, Sendable {
        public let path: String
        public let text: String
        public let fullName: String
    }

    public func diagramModel() async throws -> DiagramModel {
        try await request("diagram/model", JSONValue.null)
    }

    public func addMember(source: String, type: String, member: DiagramMember) async throws -> String? {
        try await sourceRequest("diagram/addMember", ["source": .string(source), "typeFullName": .string(type), "member": .encode(member)])
    }

    public func removeMember(source: String, type: String, signature: String) async throws -> String? {
        try await sourceRequest("diagram/removeMember", ["source": .string(source), "typeFullName": .string(type), "signature": .string(signature)])
    }

    public func updateMember(source: String, type: String, signature: String, member: DiagramMember) async throws -> String? {
        try await sourceRequest("diagram/updateMember", ["source": .string(source), "typeFullName": .string(type), "signature": .string(signature), "member": .encode(member)])
    }

    public func setBaseList(source: String, type: String, baseType: String?, interfaces: [String]) async throws -> String? {
        try await sourceRequest("diagram/setBaseList", ["source": .string(source), "typeFullName": .string(type), "baseType": .optional(baseType), "interfaces": .strings(interfaces)])
    }

    public func setModifiers(source: String, type: String, access: Access, modifiers: TypeModifiers) async throws -> String? {
        try await sourceRequest("diagram/setModifiers", ["source": .string(source), "typeFullName": .string(type), "access": .string(access.rawValue), "modifiers": .int(modifiers.rawValue)])
    }

    public func removeType(source: String, type: String) async throws -> String? {
        try await sourceRequest("diagram/removeType", ["source": .string(source), "typeFullName": .string(type)])
    }

    public func locate(source: String, type: String, signature: String?) async throws -> LocateResult {
        try await request("diagram/locate", ["source": .string(source), "typeFullName": .string(type), "signature": .optional(signature)] as JSONValue)
    }

    public func parseMember(kind: MemberKind, text: String, owner: String) async throws -> DiagramMember? {
        try await request("diagram/parseMember", ["kind": .string(kind.rawValue), "text": .string(text), "ownerName": .string(owner)] as JSONValue)
    }

    public func newType(kind: DiagramTypeKind, name: String, folder: String) async throws -> NewTypeResult {
        try await request("diagram/newType", ["kind": .string(kind.rawValue), "name": .string(name), "folder": .string(folder)] as JSONValue)
    }

    private func sourceRequest(_ method: String, _ params: JSONValue) async throws -> String? {
        let result: SourceResult = try await request(method, params)
        return result.text
    }
}
