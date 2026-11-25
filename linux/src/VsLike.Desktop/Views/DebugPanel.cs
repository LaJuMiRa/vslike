using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Desktop.Editor;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// Variables and watch, call stack and threads, and the debug console. The columns share the panel width and never ask
/// for more than it offers (DebugPanel in TerminalAndDebug.swift).
/// </summary>
public sealed class DebugPanel : UserControl
{
    public DebugPanel(Workspace workspace)
    {
        var debugger = workspace.Debugger;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,*") };
        Place(grid, 0, new VariablesPane(debugger));
        Place(grid, 1, Ui.Divider(vertical: true));
        Place(grid, 2, new CallStackPane(debugger));
        Place(grid, 3, Ui.Divider(vertical: true));
        Place(grid, 4, new DebugConsolePane(debugger));
        grid.ClipToBounds = true;
        Content = grid;
    }

    internal static Control Header(string title)
    {
        var text = Ui.Text(title.ToUpperInvariant(), 11, "TextSecondary", FontWeight.SemiBold);
        text.Margin = new Thickness(10, 6);
        return text;
    }

    private static void Place(Grid grid, int column, Control control)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}

internal sealed class VariablesPane : UserControl
{
    private readonly DebugController _debugger;

    public VariablesPane(DebugController debugger)
    {
        _debugger = debugger;
        var header = DebugPanel.Header(L.T("Variables"));
        var body = new Live(Body, debugger)
        {
            Only = new HashSet<string> { nameof(DebugController.IsStopped), nameof(DebugController.IsActive), nameof(DebugController.Variables), nameof(DebugController.Watches) },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(body);
        Content = dock;
    }

    private Control Body()
    {
        var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        if (!_debugger.IsStopped)
        {
            var status = Ui.Text(L.T(_debugger.IsActive ? "Running…" : "Not debugging"), 12, "TextSecondary");
            status.Margin = new Thickness(4, 2);
            rows.Children.Add(status);
        }

        foreach (var variable in _debugger.Variables)
        {
            rows.Children.Add(new VariableRow(variable, _debugger, 0));
        }

        var watchTitle = Ui.Text(L.T("Watch"), 11, "TextSecondary", FontWeight.SemiBold);
        watchTitle.Margin = new Thickness(4, 10, 0, 4);
        rows.Children.Add(watchTitle);
        foreach (var watch in _debugger.Watches)
        {
            rows.Children.Add(WatchRow(watch));
        }

        var add = new TextBox { PlaceholderText = L.T("Add expression"), FontFamily = Ui.Mono, FontSize = 11.5, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        add.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _debugger.AddWatch(add.Text ?? string.Empty);
                add.Text = string.Empty;
                e.Handled = true;
            }
        };
        rows.Children.Add(add);
        return new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private Control WatchRow(WatchExpression watch)
    {
        var value = Ui.Text(watch.Value, 11.5, watch.IsError ? "VsError" : "SyntaxString", mono: true);
        var remove = Ui.Plain(
            Icons.Make("minus.circle", 12, "TextSecondary"),
            () => _debugger.Watches = [.. _debugger.Watches.Where(w => w.Id != watch.Id)]);
        var left = Ui.HStack(6, Ui.Text(watch.Expression, 11.5, "TextPrimary", mono: true), Ui.Text("=", 11.5, "TextTertiary"), value);
        var row = Ui.Row([left], [remove], 4);
        row.Margin = new Thickness(4, 2);
        return row;
    }
}

/// <summary>A variable with lazily loaded children (VariableRow).</summary>
internal sealed class VariableRow : UserControl
{
    private readonly VariableNode _variable;
    private readonly DebugController _debugger;
    private readonly int _depth;
    private readonly StackPanel _children = new();
    private bool _expanded;
    private bool _loaded;
    private Control _disclosure = new Panel();

    public VariableRow(VariableNode variable, DebugController debugger, int depth)
    {
        _variable = variable;
        _debugger = debugger;
        _depth = depth;
        Build();
    }

    private void Build()
    {
        _disclosure = _variable.Reference > 0
            ? Ui.Plain(Icons.Make(_expanded ? "chevron.down" : "chevron.right", 9, "TextTertiary", 2.4), Toggle)
            : new Panel();
        _disclosure.Width = 14;
        var name = Ui.Text(_variable.Name, 11.5, "SyntaxParameter", mono: true);
        var value = Ui.Text(_variable.Value, 11.5, "TextPrimary", mono: true);
        var left = Ui.HStack(6, _disclosure, name, value);
        var trailing = new List<Control>();
        if (_variable.Type is { } type)
        {
            trailing.Add(Ui.Text(type, 10.5, "TextTertiary"));
        }

        var row = Ui.Row([left], trailing, 4);
        row.Margin = new Thickness(_depth * 14, 2, 0, 2);
        ToolTip.SetTip(row, $"{_variable.Name} = {_variable.Value}");
        Content = Ui.VStack(0, row, _children);
    }

    private void Toggle()
    {
        _expanded = !_expanded;
        _children.Children.Clear();
        if (_expanded && !_loaded)
        {
            _loaded = true;
            _ = LoadChildren();
        }
        else if (_expanded)
        {
            AddChildren(_variable.Children ?? []);
        }

        Build();
    }

    private async Task LoadChildren()
    {
        var children = await _debugger.ChildrenOf(_variable);
        AddChildren(children);
    }

    private void AddChildren(IEnumerable<VariableNode> nodes)
    {
        _children.Children.Clear();
        foreach (var child in nodes)
        {
            _children.Children.Add(new VariableRow(child, _debugger, _depth + 1));
        }
    }
}

internal sealed class CallStackPane : UserControl
{
    private readonly DebugController _debugger;

    public CallStackPane(DebugController debugger)
    {
        _debugger = debugger;
        var header = DebugPanel.Header(L.T("Call Stack"));
        var body = new Live(Body, debugger)
        {
            Only = new HashSet<string> { nameof(DebugController.Frames), nameof(DebugController.SelectedFrameId), nameof(DebugController.Threads), nameof(DebugController.ThreadId) },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(body);
        Content = dock;
    }

    private Control Body()
    {
        var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        foreach (var frame in _debugger.Frames)
        {
            rows.Children.Add(FrameRow(frame));
        }

        if (_debugger.Threads.Count > 1)
        {
            var title = Ui.Text(L.T("Threads"), 11, "TextSecondary", FontWeight.SemiBold);
            title.Margin = new Thickness(4, 10, 0, 4);
            rows.Children.Add(title);
            foreach (var thread in _debugger.Threads)
            {
                var name = thread.Name.Length == 0 ? L.T("Thread %lld", thread.Id) : thread.Name;
                var label = Ui.Label(name, thread.Id == _debugger.ThreadId ? "arrowtriangle.right.fill" : "circle", "TextPrimary", 11, 10);
                label.Margin = new Thickness(4, 2);
                rows.Children.Add(label);
            }
        }

        return new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private Control FrameRow(StackFrame frame)
    {
        var location = frame.FilePath is { } path
            ? Ui.Text($"{System.IO.Path.GetFileName(path)}:{frame.Line}", 10, "TextSecondary")
            : Ui.Text(L.T("External code"), 10, "TextTertiary");
        var stack = Ui.VStack(1, Ui.Text(frame.Name, 11.5, "TextPrimary", mono: true), location);
        var face = new Border { Child = stack, Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (_debugger.SelectedFrameId == frame.Id)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        Ui.OnTap(face, () => _debugger.SelectedFrameId = frame.Id);
        return face;
    }
}

internal sealed class DebugConsolePane : UserControl
{
    private readonly DebugController _debugger;
    private readonly LogView _log = new();
    private readonly TextBox _expression;

    public DebugConsolePane(DebugController debugger)
    {
        _debugger = debugger;
        _expression = new TextBox
        {
            PlaceholderText = L.T("Evaluate expression"),
            FontFamily = Ui.Mono,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            IsEnabled = debugger.IsStopped,
        };
        _expression.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var text = _expression.Text ?? string.Empty;
                _expression.Text = string.Empty;
                _ = _debugger.Evaluate(text);
                e.Handled = true;
            }
        };
        var input = new Border { Child = Ui.Row([Icons.Make("chevron.right", 11, "VsAccent", 2.2)], [], 6), Padding = new Thickness(8) };
        ((DockPanel)input.Child).LastChildFill = true;
        ((DockPanel)input.Child).Children.Add(_expression);
        Ui.Res(input, Border.BackgroundProperty, "BarBackground");

        var header = DebugPanel.Header(L.T("Debug Console"));
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(input, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(input);
        dock.Children.Add(_log);
        Content = dock;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _debugger.PropertyChanged += OnChanged;
        _log.Update(_debugger.Console);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _debugger.PropertyChanged -= OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DebugController.Console):
                _log.Update(_debugger.Console);
                break;
            case nameof(DebugController.IsStopped):
                _expression.IsEnabled = _debugger.IsStopped;
                break;
        }
    }
}
