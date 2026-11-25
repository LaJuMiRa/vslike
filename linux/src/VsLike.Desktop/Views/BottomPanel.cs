using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>Problems, output, terminal and debugger below the editor (BottomPanel in BottomPanel.swift).</summary>
public sealed class BottomPanel : UserControl
{
    // Not Search (typing in the filter field must keep the field) and not OutputLog.Lines (one change per log line).
    private static readonly HashSet<string> HeaderProperties =
    [
        nameof(Workspace.BottomTab), nameof(DiagnosticsStore.All), nameof(DiagnosticsStore.ErrorCount), nameof(DiagnosticsStore.StyleCopCount),
        nameof(DiagnosticsStore.CurrentFilter), nameof(TerminalController.Sessions), nameof(DebugController.IsActive),
        nameof(DebugController.Console), nameof(OutputLog.SelectedChannel),
    ];

    private readonly Workspace _workspace;
    private readonly Live _header;
    private int _level;

    public BottomPanel(Workspace workspace)
    {
        _workspace = workspace;
        _header = new Live(
            Header,
            () => [workspace, workspace.Diagnostics, workspace.Terminals, workspace.Debugger, workspace.Output])
        {
            Height = 34,
            ClipToBounds = true,
            Only = HeaderProperties,
        };
        var bar = new Border { Child = _header, Height = 34, BorderThickness = new Thickness(0, 1, 0, 1) };
        Ui.Res(bar, Border.BackgroundProperty, "BarBackground");
        Ui.Res(bar, Border.BorderBrushProperty, "Separator");

        var content = new Live(Body, workspace) { Only = new HashSet<string> { nameof(Workspace.BottomTab) } };
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(content);
        Content = dock;
        this.GetObservable(BoundsProperty).Subscribe(new BoundsObserver(this));
    }

    /// <summary>Narrow panels fall back to a compact filter, then to icon-only tabs, instead of growing wider than the window.</summary>
    private static int LevelFor(double width) => width >= 900 ? 0 : width >= 620 ? 1 : 2;

    private Control Body() => _workspace.BottomTab switch
    {
        BottomTab.Problems => new ProblemsView(_workspace),
        BottomTab.Output => new OutputPanel(_workspace),
        BottomTab.Terminal => new TerminalPanel(_workspace),
        _ => new DebugPanel(_workspace),
    };

    private Control Header()
    {
        var compactAccessory = _level >= 1;
        var iconOnly = _level >= 2;
        var tabs = new List<Control?>
        {
            TabButton("Problems", "exclamationmark.bubble", BottomTab.Problems, _workspace.Diagnostics.All.Count, iconOnly),
            TabButton("Output", "text.alignleft", BottomTab.Output, 0, iconOnly),
            TabButton("Terminal", "terminal", BottomTab.Terminal, _workspace.Terminals.Sessions.Count, iconOnly),
        };
        if (_workspace.Debugger.IsActive || _workspace.Debugger.Console.Count > 0)
        {
            tabs.Add(TabButton("Debug", "ladybug", BottomTab.Debug, 0, iconOnly));
        }

        var hide = Ui.IconButton("chevron.down", () => _workspace.IsBottomPanelVisible = false, L.T("Hide Panel (⌘J)"), 12);
        var row = Ui.Row([Ui.HStack(4, [.. tabs])], [Accessory(compactAccessory), hide], 8);
        row.Margin = new Thickness(10, 0);
        return row;
    }

    private Control TabButton(string title, string symbol, BottomTab tab, int badge, bool iconOnly)
    {
        var selected = _workspace.BottomTab == tab;
        var colorKey = selected ? "TextPrimary" : "TextSecondary";
        var content = Ui.HStack(5, Icons.Make(symbol, 13, colorKey));
        if (!iconOnly)
        {
            content.Children.Add(Ui.Text(L.T(title), 12, colorKey, selected ? FontWeight.SemiBold : FontWeight.Normal));
        }

        if (badge > 0)
        {
            var count = Ui.Text(badge.ToString(CultureInfo.InvariantCulture), 10, colorKey, FontWeight.SemiBold, mono: true);
            content.Children.Add(Ui.Rounded(count, selected ? "FillSelected" : "FillQuaternary", 8, new Thickness(5, 1)));
        }

        var face = new Border { Child = content, Height = 24, Padding = new Thickness(10, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (selected)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        return Ui.Plain(face, () => _workspace.BottomTab = tab, L.T(title));
    }

    /// <summary>Filters and actions that belong to the selected panel tab.</summary>
    private Control Accessory(bool compact)
    {
        switch (_workspace.BottomTab)
        {
            case BottomTab.Problems:
                var diagnostics = _workspace.Diagnostics;
                var filter = Segmented(
                    [
                        (L.T("All"), DiagnosticsStore.Filter.All),
                        (L.T("Errors %lld", diagnostics.ErrorCount), DiagnosticsStore.Filter.Errors),
                        (L.T("Warnings"), DiagnosticsStore.Filter.Warnings),
                        (L.T("StyleCop %lld", diagnostics.StyleCopCount), DiagnosticsStore.Filter.StyleCop),
                    ],
                    diagnostics.CurrentFilter,
                    value => diagnostics.CurrentFilter = value,
                    compact);
                var search = new TextBox { Text = diagnostics.Search, PlaceholderText = L.T("Filter"), Width = compact ? 110 : 160, MinHeight = 22 };
                search.TextChanged += (_, _) => diagnostics.Search = search.Text ?? string.Empty;
                return Ui.HStack(8, filter, search);
            case BottomTab.Output:
                var output = _workspace.Output;
                var channel = Segmented(
                    [.. Enum.GetValues<OutputLog.Channel>().Select(c => (OutputLog.Title(c), c))],
                    output.SelectedChannel,
                    value => output.SelectedChannel = value,
                    compact);
                return Ui.HStack(8, channel, Ui.IconButton("trash", () => output.Clear(output.SelectedChannel), L.T("Clear"), 13));
            case BottomTab.Terminal:
                return Ui.IconButton("plus", () => _ = _workspace.Terminals.NewShell(), L.T("New Terminal"), 13);
            default:
                return new Panel();
        }
    }

    /// <summary>Segmented filter, or a pop-up menu when the panel is narrow.</summary>
    private static Control Segmented<T>(IReadOnlyList<(string Title, T Value)> options, T selected, Action<T> changed, bool compact)
    {
        if (compact)
        {
            return Form.Picker(options, selected, changed);
        }

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var (title, value) in options)
        {
            var isSelected = EqualityComparer<T>.Default.Equals(value, selected);
            var label = Ui.Text(title, 11, "TextPrimary", isSelected ? FontWeight.SemiBold : FontWeight.Normal);
            var face = new Border { Child = label, Height = 20, Padding = new Thickness(9, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
            if (isSelected)
            {
                Ui.Res(face, Border.BackgroundProperty, "ControlBackground");
            }

            var captured = value;
            strip.Children.Add(Ui.Plain(face, () => changed(captured)));
        }

        return Ui.Rounded(strip, "FillCapsule", 6, new Thickness(1));
    }

    private void SetLevel(double width)
    {
        var level = LevelFor(width);
        if (level != _level)
        {
            _level = level;
            _header.Refresh();
        }
    }

    private sealed class BoundsObserver : IObserver<Rect>
    {
        private readonly BottomPanel _panel;

        public BoundsObserver(BottomPanel panel)
        {
            _panel = panel;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Rect value) => _panel.SetLevel(value.Width);
    }
}

/// <summary>The problem list: severity, code, message, file and line. Double-click opens the location (ProblemsView).</summary>
public sealed class ProblemsView : ReactiveView
{
    private readonly Workspace _workspace;

    public ProblemsView(Workspace workspace)
    {
        _workspace = workspace;
    }

    protected override IEnumerable<INotifyPropertyChanged?> Observed() => [_workspace.Diagnostics];

    protected override Control Render()
    {
        var problems = _workspace.Diagnostics.Visible;
        if (problems.Count == 0)
        {
            var empty = _workspace.Diagnostics.All.Count == 0;
            return Ui.Unavailable(
                L.T(empty ? "No problems" : "No matching problems"),
                "checkmark.seal",
                L.T(empty ? "Errors, warnings and StyleCop findings appear here while you type." : "Change the filter to see more."));
        }

        var list = new ListBox { ItemsSource = problems, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<VsLike.Kit.Diagnostic>((d, _) => Row(d)), Background = Brushes.Transparent };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is VsLike.Kit.Diagnostic diagnostic)
            {
                _workspace.Navigate(diagnostic);
            }
        };
        list.ContextMenu = new ContextMenu();
        list.ContextMenu.Opening += (_, _) =>
            list.ContextMenu.ItemsSource = list.SelectedItem is VsLike.Kit.Diagnostic diagnostic ? Ui.Menu(MenuEntries(diagnostic)) : new List<Control>();

        var header = ColumnHeader();
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(list);
        return dock;
    }

    private static Grid Columns() => new() { ColumnDefinitions = new ColumnDefinitions("22,74,*,180,100"), Margin = new Thickness(10, 0) };

    private static Control ColumnHeader()
    {
        var grid = Columns();
        string[] titles = [string.Empty, L.T("Code"), L.T("Message"), L.T("File"), L.T("Project")];
        for (var i = 0; i < titles.Length; i++)
        {
            var text = Ui.Text(titles[i], 11, "TextSecondary", FontWeight.SemiBold);
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        var box = new StackPanel { Children = { new Border { Child = grid, Height = 22 }, Ui.Divider(opacity: 0.6) } };
        return box;
    }

    private Control Row(VsLike.Kit.Diagnostic diagnostic)
    {
        var grid = Columns();
        var message = Ui.Text(diagnostic.Message, 12);
        message.TextWrapping = TextWrapping.Wrap;
        message.MaxHeight = 34;
        ToolTip.SetTip(message, diagnostic.Message);
        Control file = new Panel();
        if (diagnostic.FilePath is { } path)
        {
            var location = string.Create(CultureInfo.InvariantCulture, $"{System.IO.Path.GetFileName(path)}:{diagnostic.StartLine}");
            file = Ui.Tip(Ui.Text(location, 12, "TextSecondary"), path);
        }

        var project = diagnostic.ProjectName ?? _workspace.ProjectContaining(diagnostic.FilePath ?? string.Empty)?.Name ?? string.Empty;
        Control[] cells = [SeverityIcon(diagnostic), CodeBadge(diagnostic), message, file, Ui.Text(project, 12, "TextSecondary")];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        grid.Margin = new Thickness(2, 3);
        return grid;
    }

    private static Control SeverityIcon(VsLike.Kit.Diagnostic diagnostic) => diagnostic.Severity switch
    {
        VsLike.Kit.DiagnosticSeverity.Error => Icons.Make("xmark.octagon.fill", 13, "VsError"),
        VsLike.Kit.DiagnosticSeverity.Warning => diagnostic.IsStyleCop
            ? Icons.Make("checkmark.seal.fill", 13, "VsStyleCop")
            : Icons.Make("exclamationmark.triangle.fill", 13, "VsWarning"),
        _ => Icons.Make("info.circle.fill", 13, "VsInfo"),
    };

    private static string KeyFor(VsLike.Kit.Diagnostic diagnostic)
    {
        if (diagnostic.IsStyleCop)
        {
            return "VsStyleCop";
        }

        return diagnostic.Severity switch
        {
            VsLike.Kit.DiagnosticSeverity.Error => "VsError",
            VsLike.Kit.DiagnosticSeverity.Warning => "VsWarning",
            _ => "VsInfo",
        };
    }

    private static Control CodeBadge(VsLike.Kit.Diagnostic diagnostic)
    {
        var key = KeyFor(diagnostic);
        var text = Ui.Text(diagnostic.Code, 10.5, key, FontWeight.SemiBold, mono: true);
        var badge = Ui.Rounded(text, key, 4, new Thickness(6, 2));
        badge.Background = null;
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.Res(badge, Border.BorderBrushProperty, key);
        badge.BorderThickness = new Thickness(1);
        return badge;
    }

    private IEnumerable<MenuEntry?> MenuEntries(VsLike.Kit.Diagnostic diagnostic)
    {
        yield return new MenuEntry(L.T("Go to Location"), () => _workspace.Navigate(diagnostic));
        yield return new MenuEntry(
            L.T("Quick Actions and Fixes…"),
            () =>
            {
                _workspace.Navigate(diagnostic);
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _workspace.ActiveEditor?.ShowQuickActions(), TimeSpan.FromMilliseconds(300));
            });
        yield return new MenuEntry(L.T("Copy Message"), () => Ui.Copy(this, $"{diagnostic.Code}: {diagnostic.Message}"));
        if (diagnostic.HelpLink is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            yield return new MenuEntry(L.T("Open Documentation"), () => OpenUrl(uri));
        }
    }

    private static void OpenUrl(Uri uri)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", uri.AbsoluteUri) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser opener installed: nothing to open.
        }
    }
}

/// <summary>Build, test, engine and debugger output (OutputPanel).</summary>
public sealed class OutputPanel : UserControl
{
    private readonly OutputLog _output;
    private readonly LogView _log = new();

    public OutputPanel(Workspace workspace)
    {
        _output = workspace.Output;
        Content = _log;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _output.PropertyChanged += OnOutputChanged;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _output.PropertyChanged -= OnOutputChanged;
    }

    private void OnOutputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputLog.Lines) or nameof(OutputLog.SelectedChannel))
        {
            Show();
        }
    }

    private void Show() => _log.Update(_output.LinesIn(_output.SelectedChannel));
}
