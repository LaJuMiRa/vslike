namespace VsLike.Core.Code;

/// <summary>
/// Semantisches Modell der geöffneten Solution. Die Implementierung (Roslyn-Bibliothek oder später ein
/// Language Server) bleibt hinter dieser Schnittstelle austauschbar. Alle Methoden sind threadsicher und blockieren nie die UI.
/// </summary>
public interface ICodeWorkspace : IDisposable
{
    /// <summary>Raised (on a background thread) when diagnostics of a file or of the whole solution changed.</summary>
    event EventHandler<DiagnosticsChangedEventArgs>? DiagnosticsChanged;

    /// <summary>Raised (on a background thread) after a solution was loaded or reloaded.</summary>
    event EventHandler<WorkspaceLoadResult>? Loaded;

    /// <summary>Gets the path of the loaded solution or project, or null.</summary>
    string? SolutionPath { get; }

    IReadOnlyList<WorkspaceProject> Projects { get; }

    /// <summary>Loads a .sln, .slnx or .csproj. Earlier state is discarded.</summary>
    Task<WorkspaceLoadResult> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Tells the workspace about unsaved editor text. Diagnostics are recomputed debounced.</summary>
    void UpdateDocumentText(string filePath, string text);

    /// <summary>Returns true if the file belongs to a loaded project (otherwise it is handled as a loose file).</summary>
    bool ContainsDocument(string filePath);

    Task<IReadOnlyList<DiagnosticInfo>> GetDocumentDiagnosticsAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Computes diagnostics for every document of every project and raises <see cref="DiagnosticsChanged"/>.</summary>
    Task RefreshAllDiagnosticsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassifiedSpan>> ClassifyAsync(string filePath, int start, int length, CancellationToken cancellationToken);

    Task<QuickInfo?> GetQuickInfoAsync(string filePath, int offset, CancellationToken cancellationToken);
}
