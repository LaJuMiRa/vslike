using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace VsLike.Roslyn.Analyzers;

/// <summary>
/// Macht <c>stylecop.json</c> für StyleCop sichtbar, ohne die csproj zu ändern: Die nächstgelegene Datei im Projektordner
/// oder darüber (bis zum Solution-Ordner) wird als AdditionalDocument in den Solution-Snapshot gelegt.
/// <c>.editorconfig</c> kommt bereits über die Analyzer-Konfiguration des Projekts mit.
/// </summary>
internal static class StyleCopConfiguration
{
    public const string FileName = "stylecop.json";
    private const string Marker = "vslike-stylecop";

    public static Solution AddStyleCopJson(Solution solution, string? solutionPath)
    {
        var boundary = solutionPath is null ? null : Path.GetDirectoryName(solutionPath);
        foreach (var project in solution.Projects.ToList())
        {
            if (project.FilePath is null || project.AdditionalDocuments.Any(d => string.Equals(Path.GetFileName(d.FilePath), FileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Find(Path.GetDirectoryName(project.FilePath)!, boundary) is { } path)
            {
                var id = DocumentId.CreateNewId(project.Id, Marker);
                solution = solution.AddAdditionalDocument(id, FileName, SourceText.From(File.ReadAllText(path)), filePath: path);
            }
        }

        return solution;
    }

    public static Solution RemoveStyleCopJson(Solution solution)
    {
        foreach (var project in solution.Projects.ToList())
        {
            foreach (var document in project.AdditionalDocuments.Where(d => d.Id.ToString().Contains(Marker, StringComparison.Ordinal)).ToList())
            {
                solution = solution.RemoveAdditionalDocument(document.Id);
            }
        }

        return solution;
    }

    private static string? Find(string directory, string? boundary)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (boundary is not null && string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), boundary.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
            {
                return null;
            }
        }

        return null;
    }
}
