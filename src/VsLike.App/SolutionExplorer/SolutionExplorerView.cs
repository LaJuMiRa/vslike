using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VsLike.Core.Localization;

namespace VsLike.App.SolutionExplorer;

/// <summary>Baum des Solution Explorers mit Kontextmenü je Knotentyp und Verschieben per Drag &amp; Drop.</summary>
public sealed class SolutionExplorerView : UserControl
{
    private const double DragThreshold = 6;
    private readonly TreeView _tree;
    private readonly TextBlock _empty;
    private SolutionItemViewModel? _dragSource;
    private Point _dragStart;
    private bool _dragging;

    public SolutionExplorerView()
    {
        _empty = new TextBlock { Text = Loc.Get("Solution.None"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(16), Opacity = 0.6 };
        _tree = new TreeView
        {
            Background = Avalonia.Media.Brushes.Transparent,
            ItemTemplate = new FuncTreeDataTemplate<SolutionItemViewModel>(
                (item, _) => new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = item.Icon, FontSize = 11, MinWidth = 16, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = item.Detail ?? string.Empty, FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                item => item.Children),
        };
        _tree.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Avalonia.Styling.Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(SolutionItemViewModel.IsExpanded), BindingMode.TwoWay)) },
        });
        _tree.DoubleTapped += async (_, _) => await (Selected is { } item && ViewModel is { } vm ? vm.OpenAsync(item) : Task.CompletedTask).ConfigureAwait(true);
        _tree.KeyDown += OnKeyDown;
        _tree.ContextRequested += OnContextRequested;
        _tree.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Content = new Panel { Children = { _empty, _tree } };
    }

    /// <summary>Gets or sets the commands for the context menu (set by the workbench).</summary>
    public static Func<SolutionItemViewModel, IReadOnlyList<(string TitleKey, Func<Task> Run)>>? MenuProvider { get; set; }

    private SolutionExplorerViewModel? ViewModel => DataContext as SolutionExplorerViewModel;

    private SolutionItemViewModel? Selected => _tree.SelectedItem as SolutionItemViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            _tree.ItemsSource = vm.Roots;
            UpdateEmpty();
            vm.PropertyChanged += (_, _) => UpdateEmpty();
        }
    }

    private static SolutionItemViewModel? ItemAt(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()?.DataContext as SolutionItemViewModel;

    private void UpdateEmpty()
    {
        var has = ViewModel?.HasSolution == true;
        _empty.IsVisible = !has;
        _tree.IsVisible = has;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Selected is not { } item || ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.OpenAsync(item).ConfigureAwait(true);
        }
        else if (e.Key == Key.Delete || (e.Key == Key.Back && e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
            await vm.DeleteAsync(item).ConfigureAwait(true);
        }
        else if (e.Key == Key.F2)
        {
            e.Handled = true;
            await vm.RenameAsync(item).ConfigureAwait(true);
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ItemAt(e.Source) is not { } item || MenuProvider is null)
        {
            return;
        }

        _tree.SelectedItem = item;
        var items = MenuProvider(item)
            .Select(entry =>
            {
                if (entry.TitleKey == "-")
                {
                    return (Control)new Separator();
                }

                var menuItem = new MenuItem { Header = Loc.Get(entry.TitleKey) };
                menuItem.Click += async (_, _) => await entry.Run().ConfigureAwait(true);
                return menuItem;
            })
            .ToList();
        if (items.Count > 0)
        {
            new ContextMenu { ItemsSource = items }.Open(_tree);
            e.Handled = true;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = false;
        _dragSource = e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed ? ItemAt(e.Source) : null;
        _dragStart = e.GetPosition(_tree);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is { Kind: SolutionItemKind.File or SolutionItemKind.Folder } && !_dragging)
        {
            var delta = e.GetPosition(_tree) - _dragStart;
            _dragging = Math.Abs(delta.X) + Math.Abs(delta.Y) > DragThreshold;
            if (_dragging)
            {
                Cursor = new Cursor(StandardCursorType.DragMove);
            }
        }
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        var dragging = _dragging;
        _dragSource = null;
        _dragging = false;
        Cursor = Cursor.Default;
        if (!dragging || source is null || ViewModel is not { } vm || ItemAt(_tree.InputHitTest(e.GetPosition(_tree))) is not { } target)
        {
            return;
        }

        var container = target.Kind == SolutionItemKind.File
            ? vm.Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.IsContainer && n.ContainerDirectory == System.IO.Path.GetDirectoryName(target.Path))
            : target;
        if (container is { IsContainer: true })
        {
            e.Handled = true;
            await vm.MoveAsync(source, container).ConfigureAwait(true);
        }
    }
}
