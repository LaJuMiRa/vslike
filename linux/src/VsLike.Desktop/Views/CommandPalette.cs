using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>A command reachable from the palette.</summary>
public sealed record PaletteCommand(string Id, string Title, string Category, string Symbol, string? Shortcut, Action Action);

/// <summary>Spotlight-like overlay for commands (Ctrl+Shift+P) and files (Ctrl+P) (port of CommandPalette.swift).</summary>
public sealed class CommandPalette : UserControl
{
    private const int MaxEntries = 200;

    private readonly Workspace _workspace;
    private readonly MainWindow _window;
    private readonly Mode _mode;
    private readonly TextBox _query;
    private readonly StackPanel _rows = new() { Spacing = 2, Margin = new Thickness(6) };
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _empty;
    private IReadOnlyList<Entry> _entries = [];
    private int _selection;

    public CommandPalette(Workspace workspace, MainWindow window, Mode mode)
    {
        _workspace = workspace;
        _window = window;
        _mode = mode;
        _query = new TextBox
        {
            PlaceholderText = mode == Mode.Commands ? L.T("Type a command") : L.T("Go to file"),
            FontSize = 17,
            Classes = { "plain" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        _query.TextChanged += (_, _) =>
        {
            _selection = 0;
            Update();
        };
        _query.KeyDown += OnQueryKey;
        var header = Ui.HStack(10, Icons.Make(mode == Mode.Commands ? "command" : "doc.text.magnifyingglass", 16, "VsAccent"), _query);
        header.Height = 50;
        header.Margin = new Thickness(16, 0);
        _query.Width = 560;
        _scroller = new ScrollViewer { Content = _rows, MaxHeight = 380 };
        _empty = Ui.Text(L.T("No results"), 13, "TextSecondary");
        _empty.Margin = new Thickness(20);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { Width = 620, Children = { header, Ui.Divider(), _scroller, _empty } };
        var card = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 16 30 0 #47000000"),
            Margin = new Thickness(0, 80, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Ui.Res(card, Border.BackgroundProperty, "PopupBackground");
        Ui.Res(card, Border.BorderBrushProperty, "Separator");
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)) };
        scrim.PointerPressed += (_, e) =>
        {
            Dismiss();
            e.Handled = true;
        };
        Content = new Panel { Children = { scrim, card } };
        Update();
    }

    public enum Mode
    {
        Commands,
        Files,
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => _query.Focus());
    }

    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _selection = Math.Max(0, Math.Min(_entries.Count - 1, _selection + 1));
                UpdateSelection();
                break;
            case Key.Up:
                _selection = Math.Max(0, _selection - 1);
                UpdateSelection();
                break;
            case Key.Escape:
                Dismiss();
                break;
            case Key.Enter:
                Run();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Update()
    {
        _entries = Filter(AllEntries(), _query.Text ?? string.Empty);
        _rows.Children.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            var index = i;
            var row = new PaletteRow(_entries[i]);
            row.PointerPressed += (_, e) =>
            {
                _selection = index;
                Run();
                e.Handled = true;
            };
            _rows.Children.Add(row);
        }

        _empty.IsVisible = _entries.Count == 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _rows.Children.Count; i++)
        {
            ((PaletteRow)_rows.Children[i]).IsSelected = i == _selection;
        }

        if (_selection < _rows.Children.Count)
        {
            _rows.Children[_selection].BringIntoView();
        }
    }

    private static List<Entry> Filter(IReadOnlyList<Entry> all, string query)
    {
        if (query.Length == 0)
        {
            return [.. all.Take(MaxEntries)];
        }

        return
        [
            .. all
                .Select(entry => (Entry: entry, Score: Math.Max(
                    FuzzyMatcher.Score(query, entry.Title) ?? int.MinValue,
                    (FuzzyMatcher.Score(query, entry.Detail) ?? int.MinValue) == int.MinValue ? int.MinValue : FuzzyMatcher.Score(query, entry.Detail)!.Value - 15)))
                .Where(p => p.Score != int.MinValue)
                .OrderByDescending(p => p.Score)
                .Take(MaxEntries)
                .Select(p => p.Entry),
        ];
    }

    private IReadOnlyList<Entry> AllEntries()
    {
        if (_mode == Mode.Commands)
        {
            return [.. Commands().Select(c => new Entry(c.Id, c.Title, c.Category, c.Symbol, c.Shortcut, c.Action))];
        }

        var root = _workspace.RootPath ?? string.Empty;
        return
        [
            .. FileTreeBuilder.AllFiles(_workspace.FolderTree)
                .Where(node => node.Path is not null)
                .Select(node =>
                {
                    var path = node.Path!;
                    var relative = path.StartsWith(root + "/", StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;
                    return new Entry(path, Path.GetFileName(path), relative, FileNode.SymbolForFile(Path.GetFileName(path)), null, () => _workspace.OpenFile(path));
                }),
        ];
    }

    private void Run()
    {
        if (_selection < 0 || _selection >= _entries.Count)
        {
            return;
        }

        var action = _entries[_selection].Action;
        Dismiss();
        Dispatcher.UIThread.Post(action);
    }

    private void Dismiss()
    {
        _workspace.IsCommandPalettePresented = false;
        _workspace.IsQuickOpenPresented = false;
    }

    private List<PaletteCommand> Commands()
    {
        var w = _workspace;
        var editor = w.ActiveEditor;
        var list = new List<PaletteCommand>
        {
            new("build", L.T("Build Solution"), L.T("Build"), "hammer", "Ctrl+B", () => w.Build.Start(BuildKind.Build)),
            new("rebuild", L.T("Rebuild Solution"), L.T("Build"), "arrow.triangle.2.circlepath", "Ctrl+Shift+B", () => w.Build.Start(BuildKind.Rebuild)),
            new("clean", L.T("Clean Solution"), L.T("Build"), "trash", "Ctrl+Shift+K", () => w.Build.Start(BuildKind.Clean)),
            new("run", L.T("Run with Debugger"), L.T("Debug"), "play.fill", "Ctrl+R", () => _ = w.Debugger.Start()),
            new("runNoDebug", L.T("Run Without Debugging"), L.T("Build"), "play", "Ctrl+Alt+R", w.Build.StartWithoutDebugging),
            new("tests", L.T("Run All Tests"), L.T("Tests"), "checkmark.diamond", "Ctrl+U", () => w.Tests.Run()),
            new("diagram", L.T("Open Class Diagram"), L.T("View"), "rectangle.3.group", "Ctrl+Shift+D", () => _ = w.OpenSolutionDiagram()),
            new("terminal", L.T("New Terminal"), L.T("View"), "terminal", "Ctrl+`", () => _ = w.Terminals.NewShell()),
            new("problems", L.T("Show Problems"), L.T("View"), "exclamationmark.bubble", "Ctrl+Shift+M", () => w.Show(BottomTab.Problems)),
            new("output", L.T("Show Output"), L.T("View"), "text.alignleft", null, () => w.Show(BottomTab.Output)),
            new("stylecop", w.StyleCopEnabled ? L.T("Turn StyleCop Off") : L.T("Turn StyleCop On"), "StyleCop", "checkmark.seal", null, () => _ = w.ToggleStyleCop()),
            new("newFile", L.T("New File…"), L.T("File"), "doc.badge.plus", "Ctrl+N", () => w.PromptNewFile(null)),
            new("newSolution", L.T("New Solution…"), L.T("File"), "plus.square.on.square", "Ctrl+Shift+N", () => w.IsNewSolutionPresented = true),
            new("addProject", L.T("Add Project…"), L.T("File"), "shippingbox", null, () => w.NewProjectTarget = w.Solution),
            new("open", L.T("Open…"), L.T("File"), "folder", "Ctrl+O", () => _ = OpenPanel.ChooseAndOpen(w, _window)),
            new("openFolder", L.T("Open Folder…"), L.T("File"), "folder", null, () => _ = OpenPanel.ChooseAndOpenFolder(w, _window)),
            new("saveAll", L.T("Save All"), L.T("File"), "square.and.arrow.down.on.square", "Ctrl+Alt+S", w.SaveAll),
            new("restart", L.T("Restart Analysis"), L.T("Code"), "arrow.clockwise", null, () => _ = w.RestartEngine()),
            new("settings", L.T("Settings…"), "VsLike", "gearshape", "Ctrl+,", () => SettingsWindow.Show(w, _window)),
        };
        if (editor is not null && w.ActiveDocument is { } document)
        {
            list.AddRange(
            [
                new("quick", L.T("Quick Actions and Fixes"), L.T("Code"), "lightbulb", "Ctrl+Shift+.", editor.ShowQuickActions),
                new("fixStyle", L.T("Fix All StyleCop Issues in File"), "StyleCop", "wand.and.stars", "Ctrl+Alt+Shift+.", () => _ = w.FixAllStyleCop(document)),
                new("format", L.T("Format Document"), L.T("Code"), "text.alignleft", "Alt+Shift+F", () => _ = w.FormatDocument(document)),
                new("rename", L.T("Rename Symbol…"), L.T("Code"), "pencil", "F2", editor.RenameSymbol),
                new("definition", L.T("Go to Definition"), L.T("Navigate"), "arrow.right.circle", "F12", editor.GoToDefinition),
                new("references", L.T("Find References"), L.T("Navigate"), "magnifyingglass", "Shift+F12", editor.FindReferences),
                new("line", L.T("Go to Line…"), L.T("Navigate"), "number", "Ctrl+L", editor.PromptGoToLine),
                new("comment", L.T("Toggle Comment"), L.T("Code"), "text.badge.minus", "Ctrl+/", editor.ToggleComment),
                new("breakpoint", L.T("Toggle Breakpoint"), L.T("Debug"), "circle.fill", "Ctrl+\\", editor.ToggleBreakpointAtCaret),
            ]);
        }

        return list;
    }

    private sealed record Entry(string Id, string Title, string Detail, string Symbol, string? Shortcut, Action Action);

    private sealed class PaletteRow : Border
    {
        private readonly Entry _entry;

        public PaletteRow(Entry entry)
        {
            _entry = entry;
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(10, 6);
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            Build(false);
        }

        public bool IsSelected
        {
            set => Build(value);
        }

        private void Build(bool selected)
        {
            var white = Brushes.White;
            var icon = selected ? Icons.Make(_entry.Symbol, 14, white) : Icons.Make(_entry.Symbol, 14, "VsAccent");
            var iconBox = new Border { Width = 20, Child = icon };
            var title = Ui.Text(_entry.Title, 13);
            var texts = Ui.VStack(1, title);
            TextBlock? detail = null;
            if (_entry.Detail.Length > 0)
            {
                detail = Ui.Text(_entry.Detail, 11, "TextSecondary");
                detail.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
                texts.Children.Add(detail);
            }

            TextBlock? shortcut = _entry.Shortcut is null ? null : Ui.Text(_entry.Shortcut, 11, "TextSecondary");
            if (selected)
            {
                title.Foreground = white;
                detail?.Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
                shortcut?.Foreground = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
                Ui.Res(this, BackgroundProperty, "VsAccent");
            }
            else
            {
                Background = Brushes.Transparent;
            }

            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(iconBox, Dock.Left);
            row.Children.Add(iconBox);
            if (shortcut is not null)
            {
                DockPanel.SetDock(shortcut, Dock.Right);
                row.Children.Add(shortcut);
            }

            texts.Margin = new Thickness(10, 0, 10, 0);
            row.Children.Add(texts);
            Child = row;
        }
    }
}
