using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>The sidebar: a switcher for solution, files, search and tests above the matching list (SidebarView.swift).</summary>
public sealed class SidebarView : ReactiveView
{
    private readonly Workspace _workspace;

    public SidebarView(Workspace workspace)
    {
        _workspace = workspace;
    }

    public static string Symbol(SidebarTab tab) => tab switch
    {
        SidebarTab.Solution => "square.stack.3d.up",
        SidebarTab.Files => "folder",
        SidebarTab.Search => "magnifyingglass",
        _ => "checkmark.diamond",
    };

    public static string Title(SidebarTab tab) => tab switch
    {
        SidebarTab.Solution => L.T("Solution"),
        SidebarTab.Files => L.T("Files"),
        SidebarTab.Search => L.T("Search"),
        _ => L.T("Tests"),
    };

    protected override IEnumerable<INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) =>
        property is nameof(Workspace.SidebarTab) or nameof(Workspace.Tree) or nameof(Workspace.FolderTree) or null;

    protected override Control Render()
    {
        var switcher = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var tab in Enum.GetValues<SidebarTab>())
        {
            var button = TabButton(tab, _workspace.SidebarTab == tab);
            switcher.Children.Add(button);
        }

        var capsule = Ui.Rounded(switcher, "FillCapsule", 9, new Thickness(4));
        capsule.Margin = new Thickness(10, 6, 10, 8);

        Control list = _workspace.SidebarTab switch
        {
            SidebarTab.Solution => new FileTreeList(_workspace, _workspace.Tree, "No solution loaded", "solution"),
            SidebarTab.Files => new FileTreeList(_workspace, _workspace.FolderTree, "Empty folder", "files"),
            SidebarTab.Search => new SearchSidebar(_workspace, _workspace.Search),
            _ => new TestExplorer(_workspace, _workspace.Tests),
        };

        var dock = new DockPanel();
        DockPanel.SetDock(capsule, Dock.Top);
        dock.Children.Add(capsule);
        var footer = new EngineFooter(_workspace);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(list);
        return dock;
    }

    private Control TabButton(SidebarTab tab, bool selected)
    {
        var icon = Icons.Make(Symbol(tab), 14, selected ? "VsAccent" : "TextSecondary", selected ? 2.2 : 1.8);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var face = new Border { Child = icon, Height = 24, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (selected)
        {
            Ui.Res(face, Border.BackgroundProperty, "ControlBackground");
            face.BoxShadow = BoxShadows.Parse("0 0.5 1 0 #1A000000");
        }

        var button = Ui.Plain(face, () => _workspace.SidebarTab = tab, Title(tab));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinWidth = 52;
        return button;
    }
}

/// <summary>State of the analysis engine at the bottom of the sidebar (EngineFooter in SidebarView.swift).</summary>
public sealed class EngineFooter : ReactiveView
{
    private readonly Workspace _workspace;

    public EngineFooter(Workspace workspace)
    {
        _workspace = workspace;
    }

    protected override IEnumerable<INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) =>
        property is nameof(Workspace.EngineStatus) or nameof(Workspace.StyleCopEnabled) or null;

    protected override Control Render()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(14, 8) };
        switch (_workspace.EngineStatus.State)
        {
            case EngineState.Idle:
                row.Children.Add(Dot("TextSecondary"));
                row.Children.Add(Ui.Text(L.T("Analysis idle"), 11, "TextSecondary"));
                break;
            case EngineState.Starting:
            case EngineState.Loading:
                row.Children.Add(Ui.Spinner(10));
                row.Children.Add(Ui.Text(L.T("Loading solution…"), 11, "TextSecondary"));
                break;
            case EngineState.Ready:
                row.Children.Add(Dot("VsSuccess"));
                row.Children.Add(Ui.Text(L.T("IntelliSense ready"), 11, "TextSecondary"));
                if (_workspace.StyleCopEnabled)
                {
                    row.Children.Add(Ui.Text("· StyleCop", 11, "VsStyleCop"));
                }

                break;
            default:
                row.Children.Add(Dot("VsError"));
                row.Children.Add(Ui.Tip(Ui.Text(L.T("Analysis unavailable"), 11, "TextSecondary"), _workspace.EngineStatus.Message ?? string.Empty));
                var retry = Ui.Plain(Ui.Text(L.T("Retry"), 11, "VsAccent"), () => _ = _workspace.RestartEngine());
                row.Children.Add(retry);
                break;
        }

        return row;
    }

    private static Control Dot(string colorKey) =>
        Ui.Res(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, colorKey);
}
