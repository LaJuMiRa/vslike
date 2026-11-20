using VsLike.App.Editor;
using VsLike.Core.Code;

namespace VsLike.App.Intelligence;

/// <summary>
/// Wendet ein <see cref="WorkspaceEdit"/> an: offene Tabs werden im Editor geändert (ein Undo-Schritt je Datei),
/// geschlossene Dateien auf der Platte (Kodierung bleibt), neue Dateien werden angelegt und dem Workspace bekannt gemacht.
/// </summary>
public sealed class WorkspaceEditApplier
{
    private readonly DocumentManager _documents;
    private readonly ICodeWorkspace _workspace;

    public WorkspaceEditApplier(DocumentManager documents, ICodeWorkspace workspace)
    {
        _documents = documents;
        _workspace = workspace;
    }

    /// <summary>Applies edits to a string (edits use offsets of the original text).</summary>
    public static string ApplyToText(string text, IEnumerable<TextEdit> edits)
    {
        var result = text;
        foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
        {
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        }

        return result;
    }

    /// <returns>The number of files changed.</returns>
    public async Task<int> ApplyAsync(WorkspaceEdit edit)
    {
        var changed = 0;
        foreach (var file in edit.Files)
        {
            if (file.IsNewFile)
            {
                await TextFileIO.WriteAsync(file.FilePath, file.NewFileText!, new System.Text.UTF8Encoding(false)).ConfigureAwait(true);
                _workspace.AddDocument(file.FilePath, file.NewFileText!);
                changed++;
                continue;
            }

            if (file.Edits.Count == 0)
            {
                continue;
            }

            var open = _documents.OpenDocuments.FirstOrDefault(d => string.Equals(d.FilePath, file.FilePath, StringComparison.Ordinal));
            if (open is not null)
            {
                ApplyToDocument(open, file.Edits);
                _workspace.UpdateDocumentText(file.FilePath, open.TextDocument.Text);
            }
            else
            {
                var (text, encoding) = await TextFileIO.ReadAsync(file.FilePath).ConfigureAwait(true);
                var updated = ApplyToText(text, file.Edits);
                await TextFileIO.WriteAsync(file.FilePath, updated, encoding).ConfigureAwait(true);
                _workspace.UpdateDocumentText(file.FilePath, updated);
            }

            changed++;
        }

        if (edit.CaretFile is { } caretFile && edit.CaretOffset is { } caret
            && _documents.OpenDocuments.FirstOrDefault(d => d.FilePath == caretFile)?.Session is { } session)
        {
            session.Editor.CaretOffset = Math.Clamp(caret, 0, session.Editor.Document.TextLength);
        }

        return changed;
    }

    private static void ApplyToDocument(EditorDocumentViewModel document, IReadOnlyList<TextEdit> edits)
    {
        var text = document.TextDocument;
        text.BeginUpdate();
        try
        {
            foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            {
                text.Replace(edit.Start, edit.Length, edit.NewText);
            }
        }
        finally
        {
            text.EndUpdate();
        }
    }
}
