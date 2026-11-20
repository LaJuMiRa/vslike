using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VsLike.Core.Localization;

namespace VsLike.App.Output;

/// <summary>Ausgabe als virtualisierte Liste in Monospace; folgt dem Ende, solange der Nutzer nicht hochscrollt.</summary>
public sealed class OutputView : UserControl
{
    private readonly ListBox _list;
    private OutputViewModel? _viewModel;

    public OutputView()
    {
        _list = new ListBox
        {
            Background = Brushes.Transparent,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<OutputEntry>((entry, _) => new TextBlock
            {
                Text = entry?.Text,
                FontFamily = (FontFamily?)Application.Current?.FindResource("VsMonoFont") ?? FontFamily.Default,
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = entry?.IsNavigable == true ? (IBrush?)Application.Current?.FindResource(entry.Diagnostic!.Severity == Core.Code.DiagnosticSeverityKind.Error ? "VsError" : "VsWarning") : null,
                Cursor = entry?.IsNavigable == true ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            }),
        };
        _list.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Avalonia.Styling.Setter(PaddingProperty, new Thickness(8, 0)),
                new Avalonia.Styling.Setter(MinHeightProperty, 0d),
            },
        });
        _list.DoubleTapped += async (_, _) =>
        {
            if (_list.SelectedItem is OutputEntry entry && _viewModel is not null)
            {
                await _viewModel.OpenAsync(entry).ConfigureAwait(true);
            }
        };
        _list.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.C && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
            {
                await CopySelectionAsync().ConfigureAwait(true);
                e.Handled = true;
            }
        };

        var copy = new MenuItem { Header = Loc.Get("Command.Edit.Copy") };
        copy.Click += async (_, _) => await CopySelectionAsync().ConfigureAwait(true);
        _list.ContextMenu = new ContextMenu { ItemsSource = new[] { copy } };
        var clear = new Button { Content = Loc.Get("Output.Clear"), Padding = new Thickness(8, 2), FontSize = 12 };
        clear.Click += (_, _) => _viewModel?.Clear();
        var title = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(OutputViewModel.Title)));
        var header = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(clear, Avalonia.Controls.Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(title);
        var root = new DockPanel();
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.LinesAppended -= OnLinesAppended;
        }

        _viewModel = DataContext as OutputViewModel;
        _list.ItemsSource = _viewModel?.Lines;
        if (_viewModel is not null)
        {
            _viewModel.LinesAppended += OnLinesAppended;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.LinesAppended -= OnLinesAppended;
        }
    }

    private void OnLinesAppended(object? sender, EventArgs e)
    {
        if (_viewModel is { Lines.Count: > 0 } vm && IsNearEnd())
        {
            Dispatcher.UIThread.Post(() => _list.ScrollIntoView(vm.Lines.Count - 1), DispatcherPriority.Background);
        }
    }

    private bool IsNearEnd()
    {
        var scroll = _list.Scroll;
        return scroll is null || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 40;
    }

    private async Task CopySelectionAsync()
    {
        var text = string.Join('\n', _list.SelectedItems?.OfType<OutputEntry>().Select(l => l.Text) ?? []);
        if (text.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }
}
