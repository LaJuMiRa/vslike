using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VsLike.App.SolutionExplorer;

/// <summary>Ein Knoten im Solution Explorer. <see cref="Project"/> ist die csproj, zu der der Knoten gehört.</summary>
public sealed partial class SolutionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public SolutionItemViewModel(SolutionItemKind kind, string name, string path, string? project, IEnumerable<SolutionItemViewModel>? children = null)
    {
        Kind = kind;
        Name = name;
        Path = path;
        Project = project;
        Children = [.. children ?? []];
    }

    public SolutionItemKind Kind { get; }

    public string Name { get; }

    /// <summary>Gets the file system path (file, folder, csproj, sln) or the package id / referenced csproj.</summary>
    public string Path { get; }

    public string? Project { get; }

    public string? Detail { get; init; }

    public ObservableCollection<SolutionItemViewModel> Children { get; }

    public string Key => $"{Kind}|{Path}";

    public string Icon => Kind switch
    {
        SolutionItemKind.Solution => "◆",
        SolutionItemKind.SolutionFolder => "▣",
        SolutionItemKind.Project => "▤",
        SolutionItemKind.Dependencies => "⧉",
        SolutionItemKind.ProjectReference => "↗",
        SolutionItemKind.Package => "▦",
        SolutionItemKind.Folder => string.Empty,
        _ => System.IO.Path.GetExtension(Name).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".json" => "{}",
            ".md" => "¶",
            ".axaml" or ".xaml" or ".xml" or ".props" or ".targets" => "</>",
            _ => "·",
        },
    };

    /// <summary>Gets a value indicating whether files can be created in or dropped onto this node.</summary>
    public bool IsContainer => Kind is SolutionItemKind.Project or SolutionItemKind.Folder;

    /// <summary>Gets the directory for new files below this node.</summary>
    public string? ContainerDirectory => Kind switch
    {
        SolutionItemKind.Project => System.IO.Path.GetDirectoryName(Path),
        SolutionItemKind.Folder => Path,
        SolutionItemKind.File => System.IO.Path.GetDirectoryName(Path),
        _ => null,
    };

    public IEnumerable<SolutionItemViewModel> SelfAndDescendants() => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);
}
