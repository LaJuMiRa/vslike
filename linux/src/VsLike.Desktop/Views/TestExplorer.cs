using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Test explorer: tree of test projects, namespaces, classes and tests with their outcome (TestExplorer in SearchAndTests.swift).</summary>
public sealed class TestExplorer : UserControl
{
    private readonly Workspace _workspace;
    private readonly TestController _tests;
    private readonly HashSet<string> _expanded = [];
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Border _failure = new();

    public TestExplorer(Workspace workspace, TestController tests)
    {
        _workspace = workspace;
        _tests = tests;
        var toolbar = new Live(
            Toolbar,
            tests) { Only = new HashSet<string> { nameof(TestController.IsBusy), nameof(TestController.Results), nameof(TestController.Tests) } };
        toolbar.Margin = new Thickness(14, 0, 14, 6);

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        if (tests.TestProjects.Count == 0)
        {
            dock.Children.Add(Ui.Unavailable(L.T("No test projects"), "checkmark.diamond", L.T("Add an xUnit, NUnit or MSTest project to the solution.")));
        }
        else
        {
            DockPanel.SetDock(_failure, Dock.Bottom);
            dock.Children.Add(_failure);
            dock.Children.Add(_scroller);
            Fill();
        }

        Content = dock;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tests.PropertyChanged += OnTestsChanged;
        if (_tests.TotalCount == 0 && _tests.TestProjects.Count > 0)
        {
            _tests.Discover();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tests.PropertyChanged -= OnTestsChanged;
    }

    private static TestNode? Find(string id, IEnumerable<TestNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            if (node.Children is not null && Find(id, node.Children) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string FormatDuration(double seconds) =>
        seconds >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds:0.##}s")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds * 1000:0}ms");

    private void OnTestsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestController.Tree) or nameof(TestController.SelectedId) or nameof(TestController.Results) or nameof(TestController.Running))
        {
            Fill();
        }
    }

    private Control Toolbar()
    {
        var tests = _tests;
        var rerun = Ui.IconButton("arrow.clockwise", () => tests.Run(onlyFailed: true), L.T("Run Failed Tests"));
        if (tests.FailedCount == 0)
        {
            Dim(rerun);
        }

        var buttons = Ui.HStack(
            10,
            Ui.IconButton("play.fill", () => tests.Run(), L.T("Run All Tests (⌘U)")),
            rerun,
            Ui.IconButton("magnifyingglass", tests.Discover, L.T("Discover Tests")));
        var trailing = new List<Control>();
        if (tests.IsBusy)
        {
            trailing.Add(Ui.Spinner(10));
        }

        if (tests.TotalCount > 0)
        {
            trailing.Add(Ui.HStack(
                6,
                Ui.Label(tests.PassedCount.ToString(CultureInfo.InvariantCulture), "checkmark.circle.fill", "VsSuccess", 11, 11),
                Ui.Label(tests.FailedCount.ToString(CultureInfo.InvariantCulture), "xmark.circle.fill", tests.FailedCount > 0 ? "VsError" : "TextSecondary", 11, 11)));
        }

        return Ui.Row([buttons], trailing);
    }

    private static Control Dim(Control control)
    {
        control.IsEnabled = false;
        control.Opacity = 0.4;
        return control;
    }

    private void Fill()
    {
        var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        AddRows(rows, _tests.Tree, 0);
        var offset = _scroller.Offset;
        _scroller.Content = rows;
        _scroller.Offset = offset;
        _failure.Child = FailureDetail();
    }

    private void AddRows(StackPanel rows, IEnumerable<TestNode> nodes, int depth)
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

    private void Toggle(TestNode node)
    {
        if (!_expanded.Remove(node.Id))
        {
            _expanded.Add(node.Id);
        }

        Fill();
    }

    private Control Row(TestNode node, int depth)
    {
        var hasChildren = node.Children is { Count: > 0 };
        Control disclosure = hasChildren
            ? Ui.Plain(Icons.Make(_expanded.Contains(node.Id) ? "chevron.down" : "chevron.right", 9, "TextTertiary", 2.4), () => Toggle(node))
            : new Panel();
        disclosure.Width = 14;
        var trailing = new List<Control>();
        if (_tests.ResultFor(node) is { Duration: > 0 } result)
        {
            trailing.Add(Ui.Text(FormatDuration(result.Duration), 10, "TextTertiary", mono: true));
        }

        if (node.Kind is TestNode.NodeKind.Test or TestNode.NodeKind.TestCase or TestNode.NodeKind.TestClass)
        {
            trailing.Add(Ui.IconButton("play.circle", () => _tests.Run([node]), L.T("Run"), 13));
        }

        var row = Ui.Row([Ui.HStack(6, disclosure, OutcomeIcon.Make(node.Outcome), Ui.Text(node.Name, 13))], trailing, 4);
        var face = new Border { Child = row, Height = 24, Padding = new Thickness(4 + (depth * 14), 0, 6, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (_tests.SelectedId == node.Id)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        Ui.OnTap(face, () => _tests.SelectedId = node.Id);
        Ui.OnDoubleTap(face, () => _ = _tests.Open(node));
        return Ui.ContextMenu(face, () =>
        [
            new MenuEntry(L.T("Run"), () => _tests.Run([node]), "play"),
            new MenuEntry(L.T("Go to Test"), () => _ = _tests.Open(node), "arrow.right.circle"),
        ]);
    }

    private Control? FailureDetail()
    {
        if (_tests.SelectedId is not { } id || Find(id, _tests.Tree) is not { } node
            || _tests.ResultFor(node) is not { Outcome: TestOutcome.Failed } result)
        {
            return null;
        }

        var message = Ui.Text(result.ErrorMessage ?? string.Empty, 11, "TextPrimary", mono: true);
        message.TextWrapping = TextWrapping.Wrap;
        message.TextTrimming = TextTrimming.None;
        var stack = Ui.VStack(
            6,
            Ui.Divider(),
            Ui.Label(node.Name, "xmark.circle.fill", "VsError"),
            new ScrollViewer { Content = message, MaxHeight = 120, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        if (TestService.FirstSourceFrame(result.StackTrace) is { } frame)
        {
            var link = Ui.Plain(
                Ui.Text(string.Create(CultureInfo.InvariantCulture, $"{System.IO.Path.GetFileName(frame.File)}:{frame.Line}"), 11, "VsAccent"),
                () => _workspace.OpenFile(frame.File, frame.Line));
            stack.Children.Add(link);
        }

        stack.Margin = new Thickness(12, 0, 12, 8);
        return stack;
    }
}

/// <summary>The icon of a test outcome (OutcomeIcon in SearchAndTests.swift).</summary>
public static class OutcomeIcon
{
    public static Control Make(TestOutcome outcome)
    {
        Control icon = outcome switch
        {
            TestOutcome.Passed => Icons.Make("checkmark.circle.fill", 14, "VsSuccess"),
            TestOutcome.Failed => Icons.Make("xmark.circle.fill", 14, "VsError"),
            TestOutcome.Skipped => Icons.Make("minus.circle.fill", 14, "VsWarning"),
            TestOutcome.Running => Ui.Spinner(12),
            _ => Icons.Make("circle.dotted", 14, "TextTertiary"),
        };
        icon.Width = 14;
        icon.Height = 14;
        return icon;
    }
}
