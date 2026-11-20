using Microsoft.CodeAnalysis;
using VsLike.Core.Code;

namespace VsLike.Roslyn.Features;

/// <summary>Übersetzt den Unterschied zweier Solutions in ein <see cref="WorkspaceEdit"/>.</summary>
internal static class SolutionDiff
{
    public static async Task<WorkspaceEdit> CreateAsync(Solution oldSolution, Solution newSolution, CancellationToken cancellationToken)
    {
        var files = new List<FileEdit>();
        var changes = newSolution.GetChanges(oldSolution);
        foreach (var project in changes.GetProjectChanges())
        {
            foreach (var id in project.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true))
            {
                var oldDocument = oldSolution.GetDocument(id)!;
                var newDocument = newSolution.GetDocument(id)!;
                var textChanges = await newDocument.GetTextChangesAsync(oldDocument, cancellationToken).ConfigureAwait(false);
                if (oldDocument.FilePath is { } path)
                {
                    files.Add(new FileEdit(path, [.. textChanges.Select(c => new TextEdit(c.Span.Start, c.Span.Length, c.NewText ?? string.Empty))]));
                }
            }

            foreach (var id in project.GetAddedDocuments())
            {
                var document = newSolution.GetDocument(id)!;
                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var path = document.FilePath ?? Path.Combine(Path.GetDirectoryName(project.NewProject.FilePath) ?? string.Empty, document.Name);
                files.Add(new FileEdit(path, [], text.ToString()));
            }
        }

        return new WorkspaceEdit(files);
    }
}
