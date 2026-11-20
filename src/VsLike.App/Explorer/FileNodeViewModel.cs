using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Files;

namespace VsLike.App.Explorer;

public sealed partial class FileNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public FileNodeViewModel(FileTreeNode node, IReadOnlySet<string> expanded)
    {
        Name = node.Name;
        FullPath = node.FullPath;
        IsDirectory = node.IsDirectory;
        _isExpanded = expanded.Contains(node.FullPath);
        Children = [.. node.Children.Select(c => new FileNodeViewModel(c, expanded))];
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Icon => Path.GetExtension(Name).ToLowerInvariant() switch
    {
        _ when IsDirectory => string.Empty,
        ".cs" => "C#",
        ".csproj" or ".slnx" or ".sln" => "◆",
        ".json" => "{}",
        ".md" => "¶",
        _ => "·",
    };

    public ObservableCollection<FileNodeViewModel> Children { get; }

    public IEnumerable<FileNodeViewModel> SelfAndDescendants()
        => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);
}
