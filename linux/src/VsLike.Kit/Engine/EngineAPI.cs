using System.Text.Json.Nodes;

namespace VsLike.Kit;

/// <summary>Typed calls of the engine protocol. Paths are absolute file paths.</summary>
public sealed partial class EngineClient
{
    public Task<EngineInfo> Initialize(string? culture) =>
        Request<EngineInfo>("initialize", new { culture }, TimeSpan.FromSeconds(60));

    public Task<WorkspaceLoadResult> Load(string path) => Request<WorkspaceLoadResult>("workspace/load", new { path });

    public Task<IReadOnlyList<WorkspaceProject>> Projects() => Request<IReadOnlyList<WorkspaceProject>>("workspace/projects", null);

    /// <summary>Pushes unsaved editor text; diagnostics follow as <c>workspace/diagnostics</c> notifications.</summary>
    public void UpdateDocument(string path, string text) => Notify("document/update", new JsonObject { ["path"] = path, ["text"] = text });

    public Task<bool> AddDocument(string path, string text) => Request<bool>("document/add", new { path, text });

    public Task<bool> RemoveDocument(string path) => Request<bool>("document/remove", new { path });

    public Task<IReadOnlyList<Diagnostic>> Diagnostics(string path) => Request<IReadOnlyList<Diagnostic>>("document/diagnostics", new { path });

    public Task RefreshAllDiagnostics() => RawRequest("workspace/refreshDiagnostics", null);

    public Task<IReadOnlyList<ClassifiedSpan>> Classify(string path, int start, int length, CancellationToken cancellationToken = default) =>
        Request<IReadOnlyList<ClassifiedSpan>>("document/classify", new { path, start, length }, cancellationToken: cancellationToken);

    public Task<QuickInfo?> QuickInfo(string path, int offset, CancellationToken cancellationToken = default) =>
        Request<QuickInfo?>("document/quickInfo", new { path, offset }, cancellationToken: cancellationToken);

    /// <summary>Loads or unloads StyleCop (downloads the pinned package into the NuGet cache when needed).</summary>
    public Task<bool> SetStyleCop(bool enabled) => Request<bool>("styleCop/set", new { enabled }, TimeSpan.FromSeconds(300));

    public Task<bool> ShouldTriggerCompletion(string path, int offset, char typed, CancellationToken cancellationToken = default) =>
        Request<bool>("completion/shouldTrigger", new { path, offset, typed = typed.ToString() }, cancellationToken: cancellationToken);

    public Task<CompletionList?> Completion(string path, int offset, CancellationToken cancellationToken = default) =>
        Request<CompletionList?>("completion/list", new { path, offset }, cancellationToken: cancellationToken);

    public Task<string?> CompletionDescription(string path, int handle, CancellationToken cancellationToken = default) =>
        Request<string?>("completion/description", new { path, handle }, cancellationToken: cancellationToken);

    public Task<WorkspaceEdit> ResolveCompletion(string path, int handle) => Request<WorkspaceEdit>("completion/resolve", new { path, handle });

    public Task<SignatureHelp?> SignatureHelp(string path, int offset, CancellationToken cancellationToken = default) =>
        Request<SignatureHelp?>("signatureHelp", new { path, offset }, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<CodeAction>> CodeActions(string path, int start, int length, CancellationToken cancellationToken = default) =>
        Request<IReadOnlyList<CodeAction>>("codeActions/list", new { path, start, length }, cancellationToken: cancellationToken);

    public Task<WorkspaceEdit> ResolveCodeAction(int handle) => Request<WorkspaceEdit>("codeActions/resolve", new { handle });

    public Task<WorkspaceEdit> Format(string path) => Request<WorkspaceEdit>("document/format", new { path });

    public Task<IReadOnlyList<SourceLocation>> Definition(string path, int offset) => Request<IReadOnlyList<SourceLocation>>("nav/definition", new { path, offset });

    public Task<IReadOnlyList<SourceLocation>> References(string path, int offset) => Request<IReadOnlyList<SourceLocation>>("nav/references", new { path, offset });

    public Task<SourceLocation?> Declaration(string typeName, string? memberName) =>
        Request<SourceLocation?>("nav/declaration", new JsonObject { ["typeName"] = typeName, ["memberName"] = memberName });

    public Task<string?> EnclosingType(string path, int offset) => Request<string?>("document/enclosingType", new { path, offset });

    public Task<RenameTarget?> PrepareRename(string path, int offset) => Request<RenameTarget?>("rename/prepare", new { path, offset });

    public Task<WorkspaceEdit> Rename(string path, int offset, string newName) => Request<WorkspaceEdit>("rename/apply", new { path, offset, newName });
}

/// <summary>Class diagram calls. Writers take the current source and return the changed source (null deletes the file).</summary>
public sealed partial class EngineClient
{
    public Task<DiagramModel> DiagramModel() => Request<DiagramModel>("diagram/model", null);

    public Task<string?> AddMember(string source, string type, DiagramMember member) =>
        SourceRequest("diagram/addMember", new { source, typeFullName = type, member });

    public Task<string?> RemoveMember(string source, string type, string signature) =>
        SourceRequest("diagram/removeMember", new { source, typeFullName = type, signature });

    public Task<string?> UpdateMember(string source, string type, string signature, DiagramMember member) =>
        SourceRequest("diagram/updateMember", new { source, typeFullName = type, signature, member });

    public Task<string?> SetBaseList(string source, string type, string? baseType, IReadOnlyList<string> interfaces) =>
        SourceRequest("diagram/setBaseList", new JsonObject
        {
            ["source"] = source,
            ["typeFullName"] = type,
            ["baseType"] = baseType,
            ["interfaces"] = new JsonArray([.. interfaces.Select(i => (JsonNode?)JsonValue.Create(i))]),
        });

    public Task<string?> SetModifiers(string source, string type, Access access, TypeModifiers modifiers) =>
        SourceRequest("diagram/setModifiers", new { source, typeFullName = type, access, modifiers });

    public Task<string?> RemoveType(string source, string type) => SourceRequest("diagram/removeType", new { source, typeFullName = type });

    public Task<LocateResult> Locate(string source, string type, string? signature) =>
        Request<LocateResult>("diagram/locate", new JsonObject { ["source"] = source, ["typeFullName"] = type, ["signature"] = signature });

    public Task<DiagramMember?> ParseMember(MemberKind kind, string text, string owner) =>
        Request<DiagramMember?>("diagram/parseMember", new { kind, text, ownerName = owner });

    public Task<NewTypeResult> NewType(DiagramTypeKind kind, string name, string folder) =>
        Request<NewTypeResult>("diagram/newType", new { kind, name, folder });

    private async Task<string?> SourceRequest(string method, object parameters)
    {
        var result = await Request<SourceResult>(method, parameters).ConfigureAwait(false);
        return result?.Text;
    }

    private sealed record SourceResult(string? Text);
}

public sealed record LocateResult(int Line, int IdentifierOffset);

public sealed record NewTypeResult(string Path, string Text, string FullName);
