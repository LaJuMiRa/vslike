using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using VsLike.Core.Localization;

namespace VsLike.App.Testing;

/// <summary>Testbaum mit Werkzeugleiste und Detailbereich (Meldung und Stacktrace des ausgewählten Tests).</summary>
public sealed class TestExplorerView : UserControl
{
    private readonly TreeView _tree;

    public TestExplorerView()
    {
        _tree = new TreeView
        {
            Background = Brushes.Transparent,
            ItemTemplate = new FuncTreeDataTemplate<TestNodeViewModel>(
                (_, _) =>
                {
                    var glyph = new TextBlock { FontSize = 11, MinWidth = 16, VerticalAlignment = VerticalAlignment.Center };
                    glyph.Bind(TextBlock.TextProperty, new Binding(nameof(TestNodeViewModel.Glyph)));
                    glyph.Bind(TextBlock.ForegroundProperty, new Binding(nameof(TestNodeViewModel.OutcomeClass)) { Converter = OutcomeBrushConverter.Instance });
                    var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                    name.Bind(TextBlock.TextProperty, new Binding(nameof(TestNodeViewModel.Name)));
                    var duration = new TextBlock { FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
                    duration.Bind(TextBlock.TextProperty, new Binding(nameof(TestNodeViewModel.DurationText)));
                    return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { glyph, name, duration } };
                },
                node => node.Children),
        };
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(TestNodeViewModel.IsExpanded), BindingMode.TwoWay)) },
        });
        _tree.Bind(TreeView.SelectedItemProperty, new Binding(nameof(TestExplorerViewModel.Selected), BindingMode.TwoWay));
        _tree.DoubleTapped += async (_, _) =>
        {
            if (DataContext is TestExplorerViewModel vm && vm.Selected is { } node)
            {
                await vm.OpenAsync(node).ConfigureAwait(true);
            }
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 6),
            Children =
            {
                ToolButton("Tests.Discover", vm => vm.DiscoverAsync()),
                ToolButton("Tests.RunAll", vm => vm.RunAsync()),
                ToolButton("Tests.RunSelected", vm => vm.RunAsync(vm.Selected)),
                ToolButton("Tests.RunFailed", vm => vm.RunFailedAsync()),
                ToolButton("Tests.Cancel", vm =>
                {
                    vm.Cancel();
                    return Task.CompletedTask;
                }),
            },
        };
        var summary = new TextBlock { Classes = { "muted" }, Margin = new Thickness(10, 0, 8, 4), FontSize = 12 };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(TestExplorerViewModel.Summary)));
        var empty = new TextBlock { Text = Loc.Get("Tests.None"), Classes = { "muted" }, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
        empty.Bind(IsVisibleProperty, new Binding(nameof(TestExplorerViewModel.HasTestProjects)) { Converter = Avalonia.Data.Converters.BoolConverters.Not });
        var details = new SelectableTextBlock
        {
            FontFamily = (FontFamily?)Application.Current?.FindResource("VsMonoFont") ?? FontFamily.Default,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8),
        };
        details.Bind(TextBlock.TextProperty, new Binding(nameof(TestExplorerViewModel.SelectedDetails)));

        var top = new DockPanel();
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(summary, Avalonia.Controls.Dock.Top);
        top.Children.Add(toolbar);
        top.Children.Add(summary);
        top.Children.Add(new Panel { Children = { _tree, empty } });

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,120") };
        var splitter = new GridSplitter { Height = 4, ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        var scroller = new ScrollViewer { Content = details };
        Grid.SetRow(top, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(scroller, 2);
        grid.Children.Add(top);
        grid.Children.Add(splitter);
        grid.Children.Add(scroller);
        Content = grid;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _tree.ItemsSource = (DataContext as TestExplorerViewModel)?.Roots;
    }

    private Button ToolButton(string key, Func<TestExplorerViewModel, Task> action)
    {
        var button = new Button { Content = Loc.Get(key), Padding = new Thickness(8, 2), FontSize = 12 };
        button.Click += async (_, _) =>
        {
            if (DataContext is TestExplorerViewModel vm)
            {
                await action(vm).ConfigureAwait(true);
            }
        };
        return button;
    }
}
