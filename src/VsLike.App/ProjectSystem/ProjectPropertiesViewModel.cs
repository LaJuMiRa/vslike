using Dock.Model.Mvvm.Controls;
using VsLike.Core.Localization;
using VsLike.Projects.ProjectFiles;

namespace VsLike.App.ProjectSystem;

/// <summary>Formular für die wichtigsten csproj-Eigenschaften (öffnet als Tab).</summary>
public sealed class ProjectPropertiesViewModel : Document
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Choices = new Dictionary<string, IReadOnlyList<string>>
    {
        ["TargetFramework"] = ["net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"],
        ["OutputType"] = ["Exe", "Library", "WinExe"],
        ["Nullable"] = ["enable", "disable", "warnings", "annotations"],
        ["ImplicitUsings"] = ["enable", "disable"],
        ["LangVersion"] = ["latest", "preview", "14", "13", "12", "default"],
        ["TreatWarningsAsErrors"] = ["true", "false"],
        ["GenerateDocumentationFile"] = ["true", "false"],
        ["InvariantGlobalization"] = ["true", "false"],
    };

    public ProjectPropertiesViewModel(string projectPath)
    {
        ProjectPath = projectPath;
        Id = "properties:" + projectPath;
        Title = Loc.Format("Properties.Title", Path.GetFileNameWithoutExtension(projectPath));
        var file = ProjectFile.Load(projectPath);
        Properties = [.. ProjectFile.EditableProperties.Select(p => new ProjectPropertyItem(p, file.GetProperty(p), Choices.GetValueOrDefault(p, [])))];
    }

    public string ProjectPath { get; }

    public IReadOnlyList<ProjectPropertyItem> Properties { get; }

    /// <summary>Writes changed properties to the csproj; returns false if nothing changed.</summary>
    public bool Save()
    {
        var changed = Properties.Where(p => p.IsChanged).ToList();
        if (changed.Count == 0)
        {
            return false;
        }

        var file = ProjectFile.Load(ProjectPath);
        foreach (var property in changed)
        {
            file.SetProperty(property.Name, property.Value.Trim());
            property.AcceptChanges();
        }

        file.Save();
        return true;
    }
}
