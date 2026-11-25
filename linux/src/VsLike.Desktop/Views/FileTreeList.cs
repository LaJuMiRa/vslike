using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// The solution or folder tree with context menus (FileTreeList, FileRow and FileContextMenu in SidebarView.swift).
/// Expanded folders are remembered per list, so the tree keeps its shape when it is rebuilt.
/// </summary>
public sealed class FileTreeList : UserControl
{
    private static readonly Dictionary<string, HashSet<string>> ExpandedByList = [];

    private readonly Workspace _workspace;
    private readonly IReadOnlyList<FileNode> _nodes;
    private readonly HashSet<string> _expanded;
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    public FileTreeList(Workspace workspace, IReadOnlyList<FileNode> nodes, string emptyText, string listId)
    {
        _workspace = workspace;
        _nodes = nodes;
        if (!ExpandedByList.TryGetValue(listId, out var expanded))
        {
            expanded = [];
            ExpandedByList[listId] = expanded;
        }

        _expanded = expanded;
        if (nodes.Count == 0)
        {
            Content = Ui.Unavailable(L.T(emptyText), "folder");
            return;
        }

        Content = _scroller;
        Fill();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _workspace.PropertyChanged += OnWorkspaceChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _workspace.PropertyChanged -= OnWorkspaceChanged;
    }

    /// <summary>The project file of a dependency node (ids are "deps:&lt;project&gt;", "ref:&lt;project&gt;&gt;…", "pkg:&lt;project&gt;&gt;…").</summary>
    private static string? ProjectPath(FileNode node)
    {
        var id = node.Id;
        if (id.StartsWith("deps:", StringComparison.Ordinal))
        {
            return id[5..];
        }

        var arrow = id.IndexOf('>', StringComparison.Ordinal);
        var colon = id.IndexOf(':', StringComparison.Ordinal);
        return arrow >= 0 && colon >= 0 && colon < arrow ? id[(colon + 1)..arrow] : null;
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Workspace.SelectedNodeId))
        {
            Fill();
        }
    }

    private void Fill()
    {
        var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        AddRows(rows, _nodes, 0);
        var offset = _scroller.Offset;
        _scroller.Content = rows;
        _scroller.Offset = offset;
    }

    private void AddRows(StackPanel rows, IEnumerable<FileNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            rows.Children.Add(Row(node, depth));
            if (node.Children is { Count: > 0 } && _expanded.Contains(node.Id))
            {
                AddRows(rows, node.Children, depth + 1);
            }
        }
    }

    private void Toggle(FileNode node)
    {
        if (!_expanded.Remove(node.Id))
        {
            _expanded.Add(node.Id);
        }

        Fill();
    }

    private Control Row(FileNode node, int depth)
    {
        var hasChildren = node.Children is { Count: > 0 };
        Control disclosure = hasChildren
            ? Ui.Plain(Icons.Make(_expanded.Contains(node.Id) ? "chevron.down" : "chevron.right", 9, "TextTertiary", 2.4), () => Toggle(node))
            : new Panel();
        disclosure.Width = 14;
        var isProject = node.Kind.Tag == FileNode.KindTag.Project;
        var name = Ui.Text(node.Name, 13, "TextPrimary", isProject ? FontWeight.Medium : FontWeight.Normal);
        var leading = Ui.HStack(6, disclosure, Icons.Make(node.Symbol, 14, Tint(node), 1.8), name);
        var row = Ui.Row([leading], Badges(node), 4);
        var face = new Border { Child = row, Height = 24, Padding = new Thickness(4 + (depth * 14), 0, 6, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (_workspace.SelectedNodeId == node.Id)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        Ui.OnTap(face, () => Select(node));
        Ui.OnDoubleTap(face, () =>
        {
            if (hasChildren)
            {
                Toggle(node);
            }
        });
        return Ui.ContextMenu(face, () => MenuEntries(node));
    }

    private void Select(FileNode node)
    {
        _workspace.SelectedNodeId = node.Id;
        if (node.Kind.Tag == FileNode.KindTag.File && node.Path is { } path)
        {
            _workspace.OpenFile(path);
        }
    }

    private string Tint(FileNode node)
    {
        switch (node.Kind.Tag)
        {
            case FileNode.KindTag.Project:
                return "VsAccent";
            case FileNode.KindTag.Folder:
            case FileNode.KindTag.SolutionFolder:
                return "FolderTint";
            case FileNode.KindTag.File:
                if (node.Name.EndsWith(".cs", StringComparison.Ordinal))
                {
                    return "SyntaxType";
                }

                return DiagramDocument.IsDiagram(node.Name) ? "VsAccent" : "TextSecondary";
            default:
                return "TextSecondary";
        }
    }

    private IEnumerable<Control> Badges(FileNode node)
    {
        if (node.Kind.Tag == FileNode.KindTag.File && node.Path is { } path)
        {
            var document = _workspace.OpenDocuments.FirstOrDefault(d => d.Path == path);
            yield return new Live(() => FileBadges(path, document), _workspace.Diagnostics, document);
        }

        if (node.Kind is { Tag: FileNode.KindTag.Project, IsTest: false } && node.Path == _workspace.Build.StartupProject)
        {
            yield return Ui.Tip(Icons.Make("play.circle.fill", 12, "VsAccent"), L.T("Startup project"));
        }
    }

    private Control FileBadges(string path, CodeDocument? document)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var problems = _workspace.Diagnostics.DiagnosticsFor(path);
        var errors = problems.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = problems.Count(d => d.Severity == DiagnosticSeverity.Warning);
        if (errors > 0)
        {
            row.Children.Add(Ui.Text(errors.ToString(CultureInfo.InvariantCulture), 10, "VsError", FontWeight.SemiBold, mono: true));
        }
        else if (warnings > 0)
        {
            row.Children.Add(Ui.Text(warnings.ToString(CultureInfo.InvariantCulture), 10, "VsWarning", mono: true));
        }

        if (document is { IsDirty: true })
        {
            row.Children.Add(Ui.Res(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, "TextSecondary"));
        }

        return row;
    }

    private IEnumerable<MenuEntry?> MenuEntries(FileNode node)
    {
        var w = _workspace;
        var path = node.Path;
        switch (node.Kind.Tag)
        {
            case FileNode.KindTag.Project when path is not null:
                var folder = System.IO.Path.GetDirectoryName(path) ?? path;
                yield return new MenuEntry(L.T("New File…"), () => w.PromptNewFile(folder), "doc.badge.plus");
                yield return new MenuEntry(L.T("New Folder…"), () => w.PromptNewFolder(folder), "folder.badge.plus");
                yield return null;
                yield return new MenuEntry(L.T("Class Diagram"), () => _ = w.OpenDiagramForFolder(folder), "rectangle.3.group");
                yield return new MenuEntry(L.T("Manage NuGet Packages…"), () => w.NugetProject = path, "cube.box");
                yield return new MenuEntry(L.T("Properties…"), () => w.PropertiesProject = path, "slider.horizontal.3");
                yield return new MenuEntry(L.T("Set as Startup Project"), () => w.Build.StartupProject = path, "play.circle");
                yield return null;
                yield return new MenuEntry(L.T("Open Project File"), () => w.OpenFile(path), "doc.text");
                yield return new MenuEntry(L.T("Reveal in Finder"), () => w.RevealInFinder(path), "folder");
                yield return null;
                yield return new MenuEntry(L.T("Remove from Solution"), () => _ = w.RemoveProjectFromSolution(path), "minus.circle");
                break;
            case FileNode.KindTag.Folder when path is not null:
                yield return new MenuEntry(L.T("New File…"), () => w.PromptNewFile(path), "doc.badge.plus");
                yield return new MenuEntry(L.T("New Folder…"), () => w.PromptNewFolder(path), "folder.badge.plus");
                yield return new MenuEntry(L.T("Class Diagram"), () => _ = w.OpenDiagramForFolder(path), "rectangle.3.group");
                yield return null;
                yield return new MenuEntry(L.T("Rename…"), () => w.PromptRename(path), "pencil");
                yield return new MenuEntry(L.T("Reveal in Finder"), () => w.RevealInFinder(path), "folder");
                yield return new MenuEntry(L.T("Open in Terminal"), () => _ = w.ShowTerminal(), "terminal");
                yield return null;
                yield return new MenuEntry(L.T("Move to Trash"), () => _ = w.MoveToTrash(path), "trash");
                break;
            case FileNode.KindTag.File when path is not null:
                yield return new MenuEntry(L.T("Open"), () => w.OpenFile(path), "doc");
                yield return new MenuEntry(L.T("Rename…"), () => w.PromptRename(path), "pencil");
                yield return new MenuEntry(L.T("Reveal in Finder"), () => w.RevealInFinder(path), "folder");
                yield return new MenuEntry(L.T("Copy Path"), () => Ui.Copy(this, path), "doc.on.doc");
                yield return null;
                yield return new MenuEntry(L.T("Move to Trash"), () => _ = w.MoveToTrash(path), "trash");
                break;
            case FileNode.KindTag.Dependencies when ProjectPath(node) is { } project:
                yield return new MenuEntry(L.T("Add Project Reference…"), () => w.PropertiesProject = project, "arrow.triangle.branch");
                yield return new MenuEntry(L.T("Manage NuGet Packages…"), () => w.NugetProject = project, "cube.box");
                break;
            case FileNode.KindTag.Reference when ProjectPath(node) is { } project && node.Kind.ReferencePath is { } reference:
                yield return new MenuEntry(L.T("Remove Reference"), () => _ = w.RemoveProjectReference(reference, project), "minus.circle");
                break;
            case FileNode.KindTag.Package when ProjectPath(node) is { } project:
                yield return new MenuEntry(L.T("Manage NuGet Packages…"), () => w.NugetProject = project, "cube.box");
                break;
            case FileNode.KindTag.SolutionFolder:
                yield return new MenuEntry(L.T("Add Project…"), () => w.NewProjectTarget = w.Solution, "plus");
                break;
        }
    }
}
