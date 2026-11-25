using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace VsLike.Desktop;

/// <summary>
/// Small builders for the code-only views (no Swift original: SwiftUI's view syntax). Colours are theme resource keys
/// from App.axaml, so every view follows light and dark mode.
/// </summary>
public static class Ui
{
    public static readonly FontFamily Mono = new(Preferences.EditorFontFamily);

    /// <summary>Binds <paramref name="property"/> to the theme resource <paramref name="key"/> (DynamicResource).</summary>
    public static T Res<T>(T control, AvaloniaProperty property, string key)
        where T : AvaloniaObject
    {
        if (control is IResourceHost host)
        {
            control.Bind(property, host.GetResourceObservable(key));
        }

        return control;
    }

    public static TextBlock Text(string text, double size = 13, string colorKey = "TextPrimary", FontWeight weight = FontWeight.Normal, bool mono = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (mono)
        {
            block.FontFamily = Mono;
        }

        return Res(block, TextBlock.ForegroundProperty, colorKey);
    }

    public static StackPanel HStack(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        panel.Children.AddRange(children.OfType<Control>());
        foreach (var child in panel.Children)
        {
            child.VerticalAlignment = child.VerticalAlignment == VerticalAlignment.Stretch ? VerticalAlignment.Center : child.VerticalAlignment;
        }

        return panel;
    }

    public static StackPanel VStack(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        panel.Children.AddRange(children.OfType<Control>());
        return panel;
    }

    /// <summary>A horizontal row whose <paramref name="fill"/> child takes the remaining width (HStack with Spacer).</summary>
    public static DockPanel Row(IEnumerable<Control> leading, IEnumerable<Control> trailing, double spacing = 6)
    {
        var dock = new DockPanel { LastChildFill = false };
        foreach (var control in leading)
        {
            control.Margin = new Thickness(0, 0, spacing, 0);
            control.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(control, Dock.Left);
            dock.Children.Add(control);
        }

        foreach (var control in trailing.Reverse())
        {
            control.Margin = new Thickness(spacing, 0, 0, 0);
            control.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(control, Dock.Right);
            dock.Children.Add(control);
        }

        return dock;
    }

    public static Border Divider(bool vertical = false, double opacity = 1)
    {
        var line = new Border { Opacity = opacity };
        if (vertical)
        {
            line.Width = 1;
        }
        else
        {
            line.Height = 1;
        }

        return Res(line, Border.BackgroundProperty, "Separator");
    }

    public static T Tip<T>(T control, string text)
        where T : Control
    {
        ToolTip.SetTip(control, Shortcuts.Linux(text));
        return control;
    }

    /// <summary>A button without chrome (SwiftUI <c>.buttonStyle(.plain)</c>).</summary>
    public static Button Plain(Control content, Action action, string? tip = null)
    {
        var button = new Button { Content = content, Classes = { "plain" } };
        button.Click += (_, _) => action();
        return tip is null ? button : Tip(button, tip);
    }

    /// <summary>An icon button with a soft hover background (SwiftUI <c>.borderless</c> or toolbar items).</summary>
    public static Button IconButton(string symbol, Action action, string? tip = null, double size = 14, string colorKey = "TextSecondary", bool toolbar = false)
    {
        var button = new Button { Content = Icons.Make(symbol, size, colorKey), Classes = { "borderless" } };
        if (toolbar)
        {
            button.Classes.Add("toolbar");
        }

        button.Click += (_, _) => action();
        return tip is null ? button : Tip(button, tip);
    }

    public static Button PushButton(string title, Action action, bool prominent = false)
    {
        var button = new Button { Content = title };
        if (prominent)
        {
            button.Classes.Add("prominent");
        }

        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A rounded background (SwiftUI <c>.background(…, in: RoundedRectangle)</c>).</summary>
    public static Border Rounded(Control child, string backgroundKey, double radius, Thickness padding = default)
    {
        var border = new Border { Child = child, CornerRadius = new CornerRadius(radius), Padding = padding };
        return Res(border, Border.BackgroundProperty, backgroundKey);
    }

    /// <summary>Label with icon and text (SwiftUI <c>Label</c> with <c>.titleAndIcon</c>).</summary>
    public static StackPanel Label(string text, string symbol, string colorKey = "TextPrimary", double size = 13, double iconSize = 13)
    {
        return HStack(5, Icons.Make(symbol, iconSize, colorKey), Text(text, size, colorKey));
    }

    /// <summary>
    /// ContentUnavailableView: a large dimmed symbol, a title and an optional description, centred.
    /// </summary>
    public static Control Unavailable(string title, string symbol, string? description = null, Control? actions = null)
    {
        var stack = VStack(
            8,
            Icons.Make(symbol, 34, "TextTertiary", 1.5),
            Text(title, 17, "TextSecondary", FontWeight.SemiBold),
            description is null ? null : Wrap(Text(description, 12, "TextSecondary")),
            actions);
        foreach (var child in stack.Children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
        }

        stack.HorizontalAlignment = HorizontalAlignment.Center;
        stack.VerticalAlignment = VerticalAlignment.Center;
        stack.MaxWidth = 320;
        stack.Margin = new Thickness(16);
        return stack;
    }

    public static TextBlock Wrap(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.TextAlignment = TextAlignment.Center;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    /// <summary>A small indeterminate spinner (ProgressView with <c>.controlSize(.mini)</c>).</summary>
    public static Control Spinner(double size = 12) => new Spinner { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Copies text to the clipboard of the window that owns <paramref name="anchor"/>.</summary>
    public static void Copy(Visual anchor, string text)
    {
        if (TopLevel.GetTopLevel(anchor)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    /// <summary>Attaches a context menu built from (title, symbol, action) entries; null entries are separators.</summary>
    public static T ContextMenu<T>(T control, Func<IEnumerable<MenuEntry?>> entries)
        where T : Control
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) => menu.ItemsSource = Menu(entries());
        control.ContextMenu = menu;
        return control;
    }

    public static List<Control> Menu(IEnumerable<MenuEntry?> entries)
    {
        var items = new List<Control>();
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                if (items.Count > 0 && items[^1] is not Separator)
                {
                    items.Add(new Separator());
                }

                continue;
            }

            items.Add(entry.Build());
        }

        return items;
    }

    public static void OnDoubleTap(Control control, Action action) => control.DoubleTapped += (_, e) =>
    {
        action();
        e.Handled = true;
    };

    public static void OnTap(Control control, Action action) => control.PointerPressed += (_, e) =>
    {
        if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            action();
        }
    };
}

/// <summary>A context or main menu entry; <see cref="Children"/> makes a submenu.</summary>
public sealed record MenuEntry(string Title, Action? Action = null, string? Symbol = null, string? Gesture = null, bool Enabled = true, bool? Checked = null, IReadOnlyList<MenuEntry?>? Children = null)
{
    public MenuItem Build()
    {
        var item = new MenuItem { Header = Title, IsEnabled = Enabled };
        if (Symbol is not null)
        {
            item.Icon = Icons.Make(Symbol, 14, "TextPrimary");
        }

        if (Checked is { } isChecked)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = isChecked;
        }

        if (Gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(Gesture);
        }

        if (Children is not null)
        {
            item.ItemsSource = Ui.Menu(Children);
        }
        else if (Action is not null)
        {
            item.Click += (_, _) => Action();
        }

        return item;
    }
}

/// <summary>
/// A view that rebuilds its content whenever one of its observed models changes (the SwiftUI observation model).
/// Changes are coalesced into one rebuild per UI-thread turn; observed models are re-collected after each rebuild.
/// </summary>
public abstract class ReactiveView : UserControl
{
    private readonly List<INotifyPropertyChanged> _observed = [];
    private bool _pending;
    private bool _attached;

    protected ReactiveView()
    {
        ClipToBounds = false;
    }

    /// <summary>Rebuilds the content now (or after the current turn when called during a change notification).</summary>
    public void Refresh()
    {
        if (_pending)
        {
            return;
        }

        _pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pending = false;
            if (_attached)
            {
                Rebuild();
            }
        });
    }

    /// <summary>The models whose changes rebuild this view.</summary>
    protected abstract IEnumerable<INotifyPropertyChanged?> Observed();

    /// <summary>Whether a change of <paramref name="property"/> on <paramref name="sender"/> rebuilds the view (default: all).</summary>
    protected virtual bool IsRelevant(object? sender, string? property) => true;

    /// <summary>Builds the content from the current state.</summary>
    protected abstract Control Render();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Unsubscribe();
    }

    private void Rebuild()
    {
        Unsubscribe();
        Content = Render();
        foreach (var model in Observed().OfType<INotifyPropertyChanged>().Distinct())
        {
            model.PropertyChanged += OnModelChanged;
            _observed.Add(model);
        }
    }

    private void Unsubscribe()
    {
        foreach (var model in _observed)
        {
            model.PropertyChanged -= OnModelChanged;
        }

        _observed.Clear();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsRelevant(sender, e.PropertyName))
        {
            Refresh();
        }
    }
}

/// <summary>
/// An inline <see cref="ReactiveView"/>: <c>new Live(() =&gt; …, workspace, workspace.Build)</c>. With
/// <see cref="Only"/> set, only changes of those property names rebuild it.
/// </summary>
public sealed class Live : ReactiveView
{
    private readonly Func<Control> _render;
    private readonly Func<IEnumerable<INotifyPropertyChanged?>> _sources;

    public Live(Func<Control> render, params INotifyPropertyChanged?[] sources)
        : this(render, () => sources)
    {
    }

    public Live(Func<Control> render, Func<IEnumerable<INotifyPropertyChanged?>> sources)
    {
        _render = render;
        _sources = sources;
    }

    /// <summary>Gets or sets the property names that rebuild the view; null means every change.</summary>
    public IReadOnlySet<string>? Only { get; init; }

    protected override IEnumerable<INotifyPropertyChanged?> Observed() => _sources();

    protected override Control Render() => _render();

    protected override bool IsRelevant(object? sender, string? property) =>
        Only is null || property is null || Only.Contains(property);
}

/// <summary>Converts the macOS shortcut glyphs in texts (⌘, ⇧, ⌥, ⌃) to Linux notation (Ctrl, Shift, Alt).</summary>
public static class Shortcuts
{
    private const string Modifiers = "⌃⌥⇧⌘";

    /// <summary>
    /// "⇧⌘P" → "Ctrl+Shift+P", "⌃⌘R" → "Ctrl+Alt+R" (⌃⌘ becomes Ctrl+Alt so it differs from ⌘), "⌃`" → "Ctrl+`".
    /// </summary>
    public static string Linux(string text)
    {
        if (text.IndexOfAny(Modifiers.ToCharArray()) < 0)
        {
            return text;
        }

        var result = new System.Text.StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (!Modifiers.Contains(text[i], StringComparison.Ordinal))
            {
                result.Append(text[i++]);
                continue;
            }

            var mods = new HashSet<char>();
            while (i < text.Length && Modifiers.Contains(text[i], StringComparison.Ordinal))
            {
                mods.Add(text[i++]);
            }

            var parts = new List<string>();
            if (mods.Contains('⌘') || mods.Contains('⌃'))
            {
                parts.Add("Ctrl");
            }

            if (mods.Contains('⌥') || (mods.Contains('⌘') && mods.Contains('⌃')))
            {
                parts.Add("Alt");
            }

            if (mods.Contains('⇧'))
            {
                parts.Add("Shift");
            }

            result.Append(string.Join('+', parts)).Append('+');
        }

        return result.ToString();
    }
}

/// <summary>A small rotating arc (SwiftUI <c>ProgressView()</c> with a mini or small control size).</summary>
public sealed class Spinner : Control
{
    private readonly DispatcherTimer _timer;
    private double _angle;

    public Spinner()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) =>
        {
            _angle = (_angle + 30) % 360;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var brush = this.TryFindResource("TextSecondary", ActualThemeVariant, out var value) && value is IBrush b ? b : Brushes.Gray;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size / 2) - 1.2;
        for (var i = 0; i < 8; i++)
        {
            var angle = ((i * 45) + _angle) * Math.PI / 180;
            var inner = new Point(center.X + (Math.Cos(angle) * radius * 0.45), center.Y + (Math.Sin(angle) * radius * 0.45));
            var outer = new Point(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius));
            var pen = new Pen(brush, Math.Max(1.2, size / 9), lineCap: PenLineCap.Round);
            using (context.PushOpacity(0.25 + (0.75 * i / 7)))
            {
                context.DrawLine(pen, inner, outer);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? 12 : Width, double.IsNaN(Height) ? 12 : Height);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }
}
