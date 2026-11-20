using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Documents;
using VsLike.Core.Files;

namespace VsLike.App.Explorer;

/// <summary>Dateibaum des geöffneten Ordners; aktualisiert sich bei Änderungen auf der Festplatte.</summary>
public sealed partial class ExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IDocumentNavigator _navigator;
    private FolderWatcher? _watcher;

    [ObservableProperty]
    private string? _rootPath;

    public ExplorerViewModel(IDocumentNavigator navigator)
    {
        _navigator = navigator;
    }

    /// <summary>Raised on the UI thread with changed paths (after the tree has been refreshed).</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesChanged;

    public ObservableCollection<FileNodeViewModel> Roots { get; } = [];

    public bool HasFolder => RootPath is not null;

    public void OpenFolder(string path)
    {
        _watcher?.Dispose();
        RootPath = Path.GetFullPath(path);
        Refresh();
        _watcher = new FolderWatcher(RootPath);
        _watcher.Changed += (_, paths) => Dispatcher.UIThread.Post(() =>
        {
            Refresh();
            FilesChanged?.Invoke(this, paths);
        });
        OnPropertyChanged(nameof(HasFolder));
    }

    public void Refresh()
    {
        if (RootPath is null)
        {
            return;
        }

        var expanded = Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.FullPath).ToHashSet(StringComparer.Ordinal);
        expanded.Add(RootPath);
        var tree = FileTreeBuilder.Build(RootPath);
        Roots.Clear();
        Roots.Add(new FileNodeViewModel(tree, expanded));
    }

    /// <summary>All files below the root (without ignored folders).</summary>
    public IReadOnlyList<string> AllFiles()
        => [.. Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => !n.IsDirectory).Select(n => n.FullPath)];

    public Task OpenAsync(FileNodeViewModel node)
    {
        if (node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
            return Task.CompletedTask;
        }

        return _navigator.OpenAsync(node.FullPath);
    }

    public void Dispose() => _watcher?.Dispose();

    partial void OnRootPathChanged(string? value) => OnPropertyChanged(nameof(HasFolder));
}
