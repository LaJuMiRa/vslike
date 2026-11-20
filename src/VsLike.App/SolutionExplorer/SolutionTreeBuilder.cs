using VsLike.Core.Files;
using VsLike.Core.Localization;
using VsLike.Projects.ProjectFiles;
using VsLike.Projects.Solutions;

namespace VsLike.App.SolutionExplorer;

/// <summary>Baut den Solution-Baum aus Solution-Datei, csproj-Verweisen und Dateisystem.</summary>
public static class SolutionTreeBuilder
{
    public static SolutionItemViewModel Build(SolutionModel solution, IReadOnlySet<string> expandedKeys)
    {
        var folderNodes = new Dictionary<string, List<SolutionItemViewModel>>(StringComparer.Ordinal) { ["/"] = [] };
        foreach (var folder in solution.Folders.Concat(solution.Projects.Select(p => p.SolutionFolder)).Distinct().Where(f => f != "/").OrderBy(f => f.Length))
        {
            folderNodes.TryAdd(folder, []);
        }

        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            folderNodes[project.SolutionFolder].Add(Expand(BuildProject(project.FilePath, expandedKeys), expandedKeys));
        }

        SolutionItemViewModel FolderNode(string folder)
        {
            var children = folderNodes.Keys
                .Where(f => f != folder && f.StartsWith(folder, StringComparison.Ordinal) && f[folder.Length..].TrimEnd('/').Split('/').Length == 1)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(FolderNode)
                .Concat(folderNodes[folder]);
            var name = folder.TrimEnd('/').Split('/').Last();
            return Expand(new SolutionItemViewModel(SolutionItemKind.SolutionFolder, name, folder, null, children), expandedKeys, defaultExpanded: true);
        }

        var rootChildren = folderNodes.Keys
            .Where(f => f != "/" && f.TrimEnd('/').Count(c => c == '/') == 1)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(FolderNode)
            .Concat(folderNodes["/"]);
        var root = new SolutionItemViewModel(SolutionItemKind.Solution, solution.Name, solution.FilePath, null, rootChildren)
        {
            Detail = $"{solution.Projects.Count} {Loc.Get("Solution.Projects")}",
        };
        return Expand(root, expandedKeys, defaultExpanded: true);
    }

    private static SolutionItemViewModel BuildProject(string projectPath, IReadOnlySet<string> expanded)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        List<SolutionItemViewModel> dependencies = [];
        if (File.Exists(projectPath))
        {
            var file = ProjectFile.Load(projectPath);
            dependencies.AddRange(file.ProjectReferences().Select(r => new SolutionItemViewModel(SolutionItemKind.ProjectReference, Path.GetFileNameWithoutExtension(r), r, projectPath)));
            dependencies.AddRange(file.PackageReferences().Select(p => new SolutionItemViewModel(SolutionItemKind.Package, p.Id, p.Id, projectPath) { Detail = p.Version }));
        }

        var dependencyNode = Expand(new SolutionItemViewModel(SolutionItemKind.Dependencies, Loc.Get("Solution.Dependencies"), projectPath + "#deps", projectPath, dependencies), expanded);
        var files = Directory.Exists(directory) ? FileTreeBuilder.Build(directory).Children : [];
        var fileNodes = files
            .Where(n => !string.Equals(n.FullPath, projectPath, StringComparison.Ordinal))
            .Select(n => ToNode(n, projectPath, expanded));
        return new SolutionItemViewModel(SolutionItemKind.Project, Path.GetFileNameWithoutExtension(projectPath), projectPath, projectPath, [dependencyNode, .. fileNodes]);
    }

    private static SolutionItemViewModel ToNode(FileTreeNode node, string project, IReadOnlySet<string> expanded)
        => node.IsDirectory
            ? Expand(new SolutionItemViewModel(SolutionItemKind.Folder, node.Name, node.FullPath, project, node.Children.Select(c => ToNode(c, project, expanded))), expanded)
            : new SolutionItemViewModel(SolutionItemKind.File, node.Name, node.FullPath, project);

    private static SolutionItemViewModel Expand(SolutionItemViewModel node, IReadOnlySet<string> expanded, bool defaultExpanded = false)
    {
        node.IsExpanded = expanded.Contains(node.Key) || (defaultExpanded && !expanded.Contains("!" + node.Key));
        return node;
    }
}
