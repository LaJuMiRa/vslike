using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Desktop.Diagram;
using VsLike.Desktop.Editor;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// The main window content (port of MainView.swift): sidebar, tabs with editors or diagrams, the bottom panel, the
/// inspector and the toolbar; the start screen while nothing is open. Sheets, alerts and the command palette are layers
/// of <see cref="MainWindow"/>.
/// </summary>
public sealed class MainView : ReactiveView
{
    private readonly Workspace _workspace;
    private readonly MainWindow _window;
    private bool _sidebarVisible = true;

    public MainView(Workspace workspace, MainWindow window)
    {
        _workspace = workspace;
        _window = window;
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Workspace.Tabs))
            {
                EditorRegistry.Shared.Prune(workspace.OpenDocuments);
            }
        };
    }

    /// <summary>Shows or hides the sidebar (the sidebar button and NSSplitViewController.toggleSidebar).</summary>
    public void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        Refresh();
    }

    /// <summary>Shows the sidebar (Find in Files).</summary>
    public void ShowSidebar()
    {
        if (!_sidebarVisible)
        {
            ToggleSidebar();
        }
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) =>
        property is nameof(Workspace.RootPath) or nameof(Workspace.IsInspectorVisible) or null;

    protected override Control Render()
    {
        if (!_workspace.IsOpen)
        {
            return new WelcomeView(_workspace, _window);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(_sidebarVisible ? "280,Auto,*" : "0,0,*") };
        if (_sidebarVisible)
        {
            var sidebar = new DockPanel();
            var top = TitleBar.SidebarTop(_window, ToggleSidebar);
            DockPanel.SetDock(top, Dock.Top);
            sidebar.Children.Add(top);
            sidebar.Children.Add(new SidebarView(_workspace));
            Ui.Res(sidebar, Panel.BackgroundProperty, "SidebarBackground");
            grid.ColumnDefinitions[0].MinWidth = 220;
            grid.ColumnDefinitions[0].MaxWidth = 460;
            grid.Children.Add(sidebar);
            var splitter = new GridSplitter { Width = 1, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);
        }

        var detail = new DockPanel();
        var toolbar = new WorkspaceToolbar(_workspace, _window, _sidebarVisible ? null : ToggleSidebar);
        DockPanel.SetDock(toolbar, Dock.Top);
        detail.Children.Add(toolbar);
        detail.Children.Add(DetailWithInspector());
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
        return grid;
    }

    private Control DetailWithInspector()
    {
        var area = new Grid { ColumnDefinitions = new ColumnDefinitions(_workspace.IsInspectorVisible ? "*,Auto,310" : "*") };
        area.Children.Add(new DetailArea(_workspace));
        if (_workspace.IsInspectorVisible)
        {
            var divider = Ui.Divider(vertical: true);
            Grid.SetColumn(divider, 1);
            area.Children.Add(divider);
            var inspector = new InspectorView(_workspace);
            Grid.SetColumn(inspector, 2);
            area.Children.Add(inspector);
        }

        return area;
    }
}

/// <summary>Tabs, the active editor or diagram and the bottom panel in a resizable split (DetailArea in MainView.swift).</summary>
public sealed class DetailArea : ReactiveView
{
    private readonly Workspace _workspace;
    private double _panelHeight = 260;

    public DetailArea(Workspace workspace)
    {
        _workspace = workspace;
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) => property is nameof(Workspace.IsBottomPanelVisible);

    protected override Control Render()
    {
        var editors = new DockPanel { MinHeight = 200 };
        var tabs = new Live(() => _workspace.Tabs.Count == 0 ? new Panel() : new TabBar(_workspace), _workspace)
        {
            Only = new HashSet<string> { nameof(Workspace.Tabs) },
        };
        DockPanel.SetDock(tabs, Dock.Top);
        editors.Children.Add(tabs);
        editors.Children.Add(new Live(ActiveContent, _workspace) { Only = new HashSet<string> { nameof(Workspace.Tabs), nameof(Workspace.ActiveTabId) } });

        if (!_workspace.IsBottomPanelVisible)
        {
            return editors;
        }

        var grid = new Grid { RowDefinitions = new RowDefinitions($"*,Auto,{_panelHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}") };
        grid.RowDefinitions[0].MinHeight = 200;
        grid.RowDefinitions[2].MinHeight = 120;
        grid.Children.Add(editors);
        var splitter = new GridSplitter { Height = 1, ResizeDirection = GridResizeDirection.Rows };
        splitter.DragCompleted += (_, _) => _panelHeight = grid.RowDefinitions[2].ActualHeight;
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);
        var panel = new BottomPanel(_workspace);
        Grid.SetRow(panel, 2);
        grid.Children.Add(panel);
        return grid;
    }

    private Control ActiveContent() => _workspace.ActiveTab switch
    {
        EditorTab.Code code => new CodeEditorPane(code.Document, _workspace),
        EditorTab.Diagram diagram => new DiagramEditorView((DiagramEditorModel)diagram.Editor),
        _ => new EmptyEditorView(),
    };
}

/// <summary>Shown when no tab is open: shortcuts to the most useful actions (EmptyEditorView in MainView.swift).</summary>
public sealed class EmptyEditorView : UserControl
{
    public EmptyEditorView()
    {
        var rows = Ui.VStack(
            10,
            ShortcutRow("Quick Open", "⌘P"),
            ShortcutRow("Command Palette", "⇧⌘P"),
            ShortcutRow("Quick Actions and Fixes", "⇧⌘."),
            ShortcutRow("Build", "⌘B"),
            ShortcutRow("Run with Debugger", "⌘R"),
            ShortcutRow("Class Diagram", "⇧⌘D"),
            ShortcutRow("Terminal", "⌃`"));
        var stack = Ui.VStack(22, Icons.Make("chevron.left.forwardslash.chevron.right", 44, "TextTertiary", 1.2), rows);
        stack.Children[0].HorizontalAlignment = HorizontalAlignment.Center;
        stack.HorizontalAlignment = HorizontalAlignment.Center;
        stack.VerticalAlignment = VerticalAlignment.Center;
        Content = stack;
        Ui.Res(this, BackgroundProperty, "EditorBackground");
    }

    private static Control ShortcutRow(string title, string keys)
    {
        var key = Ui.Rounded(Ui.Text(Shortcuts.Linux(keys), 13, "TextPrimary", FontWeight.Medium), "FillQuaternary", 6, new Thickness(8, 2));
        var row = Ui.Row([Ui.Text(L.T(title), 13, "TextSecondary")], [key]);
        row.Width = 300;
        return row;
    }
}

/// <summary>Slim tabs with icon, name, a dot for unsaved changes and a close button on hover (TabBar in MainView.swift).</summary>
public sealed class TabBar : UserControl
{
    public TabBar(Workspace workspace)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 5) };
        foreach (var tab in workspace.Tabs)
        {
            strip.Children.Add(new TabButton(workspace, tab));
        }

        var scroller = new ScrollViewer
        {
            Content = strip,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var dock = new DockPanel { Height = 36 };
        var divider = Ui.Divider();
        DockPanel.SetDock(divider, Dock.Bottom);
        dock.Children.Add(divider);
        dock.Children.Add(scroller);
        Ui.Res(dock, Panel.BackgroundProperty, "BarBackground");
        Content = dock;
    }
}

/// <summary>One tab (TabButton in MainView.swift).</summary>
public sealed class TabButton : ReactiveView
{
    private readonly Workspace _workspace;
    private readonly EditorTab _tab;
    private bool _hovering;

    public TabButton(Workspace workspace, EditorTab tab)
    {
        _workspace = workspace;
        _tab = tab;
        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        Ui.OnTap(this, () => workspace.ActiveTabId = tab.Id);
        Ui.ContextMenu(this, MenuEntries);
        ToolTip.SetTip(this, tab is EditorTab.Code code ? code.Document.Path : (tab as EditorTab.Diagram)?.Editor.Path);
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() =>
        [_workspace, (_tab as EditorTab.Code)?.Document];

    protected override bool IsRelevant(object? sender, string? property) =>
        sender is CodeDocument ? property is nameof(CodeDocument.IsDirty) : property is nameof(Workspace.ActiveTabId);

    protected override Control Render()
    {
        var isActive = _tab.Id == _workspace.ActiveTabId;
        Control trailing;
        if (_tab.IsDirty && !_hovering)
        {
            trailing = Ui.Res(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5) }, Border.BackgroundProperty, "TextSecondary");
        }
        else
        {
            var close = Ui.Plain(Icons.Make("xmark", 9, "TextSecondary", 2.6), () => _ = _workspace.Close(_tab));
            close.Opacity = _hovering || isActive ? 1 : 0;
            trailing = close;
        }

        var holder = new Panel { Width = 14, Height = 14, Children = { trailing } };
        trailing.HorizontalAlignment = HorizontalAlignment.Center;
        trailing.VerticalAlignment = VerticalAlignment.Center;
        var row = Ui.HStack(
            6,
            Icons.Make(_tab.Symbol, 12, isActive ? "VsAccent" : "TextSecondary", 2),
            Ui.Text(_tab.Title, 12, "TextPrimary", isActive ? FontWeight.Medium : FontWeight.Normal),
            holder);
        var background = new Border
        {
            Child = row,
            Height = 26,
            Padding = new Thickness(10, 0, 6, 0),
            CornerRadius = new CornerRadius(7),
        };
        if (isActive)
        {
            Ui.Res(background, Border.BackgroundProperty, "ControlBackground");
            background.BoxShadow = BoxShadows.Parse("0 0.5 1.5 0 #1F000000");
        }
        else if (_hovering)
        {
            Ui.Res(background, Border.BackgroundProperty, "FillHover");
        }
        else
        {
            background.Background = Brushes.Transparent;
        }

        return background;
    }

    private void SetHover(bool hovering)
    {
        _hovering = hovering;
        Refresh();
    }

    private IEnumerable<MenuEntry?> MenuEntries()
    {
        yield return new MenuEntry(L.T("Close"), () => _ = _workspace.Close(_tab));
        yield return new MenuEntry(L.T("Close Others"), () => _ = _workspace.CloseOtherTabs(_tab));
        if (_tab is EditorTab.Code code)
        {
            yield return null;
            yield return new MenuEntry(L.T("Reveal in Finder"), () => _workspace.RevealInFinder(code.Document.Path));
            yield return new MenuEntry(L.T("Copy Path"), () => Ui.Copy(this, code.Document.Path));
        }
    }
}

/// <summary>Right-hand inspector: the selected diagram type, otherwise file information (InspectorView in MainView.swift).</summary>
public sealed class InspectorView : ReactiveView
{
    private readonly Workspace _workspace;

    public InspectorView(Workspace workspace)
    {
        _workspace = workspace;
        Ui.Res(this, BackgroundProperty, "BarBackground");
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) => property is nameof(Workspace.ActiveTabId) or nameof(Workspace.Tabs);

    protected override Control Render() => _workspace.ActiveTab switch
    {
        EditorTab.Diagram diagram => new DiagramInspector((DiagramEditorModel)diagram.Editor),
        EditorTab.Code code => new FileInspector(_workspace, code.Document),
        _ => Ui.Unavailable(L.T("No Selection"), "sidebar.right"),
    };
}

/// <summary>File, problems and breakpoints of the open document (FileInspector in MainView.swift).</summary>
public sealed class FileInspector : ReactiveView
{
    private readonly Workspace _workspace;
    private readonly CodeDocument _document;

    public FileInspector(Workspace workspace, CodeDocument document)
    {
        _workspace = workspace;
        _document = document;
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() => [_document, _workspace.Diagnostics, _workspace];

    protected override bool IsRelevant(object? sender, string? property) =>
        sender is not Workspace || property is nameof(Workspace.StyleCopEnabled) or nameof(Workspace.Projects);

    protected override Control Render()
    {
        var problems = _workspace.Diagnostics.DiagnosticsFor(_document.Path);
        var file = Form.Section(
            L.T("File"),
            Form.Labeled(L.T("Name"), _document.Name),
            Form.Labeled(L.T("Language"), _document.LanguageName),
            Form.Labeled(L.T("Lines"), _document.Position(_document.Storage.TextLength).Line.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _workspace.ProjectContaining(_document.Path) is { } project ? Form.Labeled(L.T("Project"), project.Name) : null);
        var problemRows = new List<Control?>
        {
            Form.Labeled(L.T("Errors"), Count(problems, d => d.Severity == DiagnosticSeverity.Error)),
            Form.Labeled(L.T("Warnings"), Count(problems, d => d.Severity == DiagnosticSeverity.Warning && !d.IsStyleCop)),
            Form.Labeled("StyleCop", Count(problems, d => d.IsStyleCop)),
        };
        if (_workspace.StyleCopEnabled)
        {
            problemRows.Add(Ui.PushButton(L.T("Fix All StyleCop Issues"), () => _ = _workspace.FixAllStyleCop(_document)));
        }

        var breakpoints = new List<Control?>();
        if (_document.Breakpoints.Count == 0)
        {
            breakpoints.Add(Ui.Text(L.T("Click a line number to add one."), 13, "TextSecondary"));
        }

        foreach (var breakpoint in _document.Breakpoints.Values.OrderBy(b => b.Line))
        {
            var dot = Ui.Res(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4) }, Border.BackgroundProperty, "Breakpoint");
            breakpoints.Add(Ui.HStack(
                6,
                dot,
                Ui.Text(L.T("Line %lld", breakpoint.Line)),
                breakpoint.Condition is { } condition ? Ui.Text(condition, 11, "TextSecondary", mono: true) : null));
        }

        return Form.Grouped(file, Form.Section(L.T("Problems"), [.. problemRows]), Form.Section(L.T("Breakpoints"), [.. breakpoints]));
    }

    private static string Count(IReadOnlyList<Diagnostic> problems, Func<Diagnostic, bool> predicate) =>
        problems.Count(predicate).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
