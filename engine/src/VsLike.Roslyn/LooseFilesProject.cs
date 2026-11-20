using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace VsLike.Roslyn;

/// <summary>
/// Projekt für .cs-Dateien, die zu keinem geladenen Projekt gehören (z. B. „Ordner öffnen“). Referenziert die
/// Laufzeit-Assemblies der IDE, damit IntelliSense für BCL-Typen funktioniert.
/// </summary>
internal sealed class LooseFilesProject : IDisposable
{
    private readonly AdhocWorkspace _workspace = new(MefHostServices.DefaultHost);
    private readonly object _gate = new();
    private readonly ProjectId _projectId;

    public LooseFilesProject()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll")
            .Select(p => MetadataReference.CreateFromFile(p));
        var info = ProjectInfo.Create(
            ProjectId.CreateNewId("Miscellaneous"),
            VersionStamp.Create(),
            "Miscellaneous Files",
            "Miscellaneous",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references);
        _projectId = info.Id;
        _workspace.AddProject(info);
    }

    public Solution Solution => _workspace.CurrentSolution;

    /// <summary>Adds or updates a loose file and returns its document.</summary>
    public Document Upsert(string path, string text)
    {
        lock (_gate)
        {
            var solution = _workspace.CurrentSolution;
            var existing = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault();
            var updated = existing is null
                ? solution.AddDocument(DocumentId.CreateNewId(_projectId), Path.GetFileName(path), SourceText.From(text), filePath: path)
                : solution.WithDocumentText(existing, SourceText.From(text));
            _workspace.TryApplyChanges(updated);
            return _workspace.CurrentSolution.GetDocument(_workspace.CurrentSolution.GetDocumentIdsWithFilePath(path)[0])!;
        }
    }

    public Document? Find(string path)
    {
        var solution = _workspace.CurrentSolution;
        var id = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault();
        return id is null ? null : solution.GetDocument(id);
    }

    public void Dispose() => _workspace.Dispose();
}
