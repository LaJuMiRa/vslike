using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using VsLike.Core.Localization;
using VsLike.Debugging;

namespace VsLike.App.Debugging;

/// <summary>Variablen, Überwachen, Aufrufliste und Threads untereinander in aufklappbaren Abschnitten.</summary>
public sealed class DebugPanelView : UserControl
{
    private static readonly FontFamily Mono = new("JetBrains Mono, Menlo, DejaVu Sans Mono, monospace");

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not DebugPanelViewModel { Controller: var controller })
        {
            return;
        }

        var locals = new TreeView
        {
            ItemsSource = controller.Locals,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncTreeDataTemplate<VariableNodeViewModel>(
                (node, _) => new TextBlock { Text = node.Display, FontFamily = Mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, [ToolTip.TipProperty] = node.Type },
                node => node.Children),
        };
        locals.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(VariableNodeViewModel.IsExpanded), BindingMode.TwoWay)) },
        });

        var watchInput = new TextBox { Watermark = Loc.Get("Debug.AddWatch"), FontFamily = Mono, FontSize = 12 };
        watchInput.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && watchInput.Text is { Length: > 0 } text)
            {
                watchInput.Text = string.Empty;
                await controller.AddWatchAsync(text).ConfigureAwait(true);
            }
        };
        var watches = new ListBox
        {
            ItemsSource = controller.Watches,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<WatchItem>((item, _) =>
            {
                var value = new TextBlock { FontFamily = Mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                value.Bind(TextBlock.TextProperty, new Binding(nameof(WatchItem.Value)) { StringFormat = item?.Expression + " = {0}" });
                var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 10 };
                remove.Click += (_, _) => controller.RemoveWatch(item!);
                var row = new DockPanel();
                DockPanel.SetDock(remove, Avalonia.Controls.Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(value);
                return row;
            }),
        };

        var frames = new ListBox
        {
            ItemsSource = controller.Frames,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<StackFrameInfo>((frame, _) => new TextBlock
            {
                Text = frame is null ? string.Empty : frame.FilePath is null ? frame.Name : $"{frame.Name}  ({Path.GetFileName(frame.FilePath)}:{frame.Line})",
                FontFamily = Mono,
                FontSize = 12,
                Opacity = frame?.FilePath is null ? 0.6 : 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }),
        };
        frames.SelectionChanged += async (_, _) =>
        {
            if (frames.SelectedItem is StackFrameInfo frame && frame != controller.SelectedFrame)
            {
                await controller.SelectFrameAsync(frame).ConfigureAwait(true);
            }
        };

        var threads = new ListBox
        {
            ItemsSource = controller.Threads,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<DebugThread>((thread, _) => new TextBlock { Text = thread is null ? string.Empty : $"#{thread.Id}  {thread.Name}", FontSize = 12 }),
        };
        threads.SelectionChanged += async (_, _) =>
        {
            if (threads.SelectedItem is DebugThread thread && thread != controller.SelectedThread)
            {
                await controller.SelectThreadAsync(thread).ConfigureAwait(true);
            }
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Section("Debug.Locals", locals),
                    Section("Debug.Watch", new StackPanel { Spacing = 4, Children = { watchInput, watches } }),
                    Section("Debug.CallStack", frames),
                    Section("Debug.Threads", threads),
                },
            },
        };
    }

    private static Expander Section(string key, Control content) => new()
    {
        Header = Loc.Get(key),
        IsExpanded = true,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Content = content,
    };
}
