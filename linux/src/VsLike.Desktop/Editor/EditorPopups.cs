using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>
/// A borderless panel that floats next to the caret without taking focus from the editor (an NSPanel child window on
/// macOS). Drawn in the window's overlay layer so that rounded corners and the shadow work on every X11/Wayland setup.
/// </summary>
public class PopupPanel
{
    private readonly Popup _popup;
    private readonly Border _frame;

    public PopupPanel(Control content)
    {
        _frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0.5),
            ClipToBounds = true,
            Child = content,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 14, Color = Color.FromArgb(70, 0, 0, 0) }),
        };
        _popup = new Popup
        {
            Child = _frame,
            ShouldUseOverlayLayer = true,
            IsLightDismissEnabled = false,
            Focusable = false,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX,
        };
    }

    /// <summary>Gets the popup; it has to live in the editor's visual tree to open (see <see cref="EditorRegistry"/>).</summary>
    public Popup Host => _popup;

    public bool IsVisible => _popup.IsOpen;

    /// <summary>Gets the floating frame, for measuring its content.</summary>
    protected Border Frame => _frame;

    /// <summary>Shows the panel below (or above) <paramref name="anchor"/>, a rect in coordinates of <paramref name="target"/>.</summary>
    public void Show(Control target, Rect anchor, Size? size, bool above = false)
    {
        _frame.Background = Palette.Popover.Brush;
        _frame.BorderBrush = Palette.Separator.Brush;
        _frame.Width = size?.Width ?? double.NaN;
        _frame.Height = size?.Height ?? double.NaN;
        _popup.PlacementTarget = target;
        _popup.PlacementRect = anchor;
        _popup.PlacementAnchor = above ? PopupAnchor.TopLeft : PopupAnchor.BottomLeft;
        _popup.PlacementGravity = above ? PopupGravity.TopRight : PopupGravity.BottomRight;
        _popup.VerticalOffset = above ? -4 : 4;
        if (_popup.IsOpen)
        {
            _popup.Close();
        }

        _popup.Open();
    }

    public void Dismiss() => _popup.Close();
}

/// <summary>The completion list: a symbol per kind, name, inline description; the selected item's documentation below.</summary>
public sealed class CompletionPopup : PopupPanel
{
    private const double RowHeight = 22;
    private const int VisibleRows = 10;
    private readonly StackPanel _rows;
    private readonly TextBlock _detail;
    private readonly ListScrollKnob _knob = new();
    private int _selected;
    private int _first;

    public CompletionPopup()
        : this(new StackPanel(), new TextBlock())
    {
    }

    private CompletionPopup(StackPanel rows, TextBlock detail)
        : base(Layout(rows, detail, out var knob))
    {
        _rows = rows;
        _detail = detail;
        _knob = knob;
        _rows.PointerWheelChanged += (_, e) =>
        {
            Scroll(e.Delta.Y > 0 ? -3 : 3);
            e.Handled = true;
        };
    }

    public IReadOnlyList<CompletionItem> Items { get; private set; } = [];

    public Action<CompletionItem>? OnCommit { get; set; }

    public Action<CompletionItem>? OnSelectionChange { get; set; }

    public CompletionItem? Selected => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

    public void Show(IReadOnlyList<CompletionItem> items, Control target, Rect anchor)
    {
        if (items.Count == 0)
        {
            Dismiss();
            return;
        }

        Items = items;
        var preselected = items.Select((item, index) => (item, index)).FirstOrDefault(p => p.item.IsPreselected).index;
        _selected = preselected;
        _first = 0;
        ScrollToVisible(preselected);
        var monoSize = 12.0;
        var longest = items.Take(200).Select(i => MeasureMono(i.DisplayText, monoSize) + ((i.InlineDescription ?? string.Empty).Length * 6.5)).DefaultIfEmpty(200).Max();
        var width = Math.Min(560, Math.Max(280, longest + 70));
        var rows = Math.Min(items.Count, VisibleRows);
        var height = (rows * RowHeight) + 8 + (_detail.IsVisible ? 52 : 0);
        Rebuild();
        Show(target, anchor, new Size(width, height));
        if (Selected is { } item)
        {
            OnSelectionChange?.Invoke(item);
        }
    }

    public void SetDetail(string? text)
    {
        var hasText = !string.IsNullOrEmpty(text);
        _detail.Text = text ?? string.Empty;
        _detail.Foreground = Palette.SecondaryLabel.Brush;
        if (_detail.IsVisible != hasText)
        {
            _detail.IsVisible = hasText;
            if (!double.IsNaN(Frame.Height))
            {
                Frame.Height += hasText ? 52 : -52;
            }
        }
    }

    public void Move(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        _selected = Math.Max(0, Math.Min(Items.Count - 1, _selected + delta));
        ScrollToVisible(_selected);
        Rebuild();
        if (Selected is { } item)
        {
            OnSelectionChange?.Invoke(item);
        }
    }

    public new void Dismiss()
    {
        base.Dismiss();
        _detail.IsVisible = false;
    }

    private static Control Layout(StackPanel rows, TextBlock detail, out ListScrollKnob knob)
    {
        detail.FontSize = 11;
        detail.TextWrapping = TextWrapping.Wrap;
        detail.MaxLines = 4;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.Margin = new Thickness(10, 4, 10, 4);
        detail.Height = 52 - 8;
        detail.IsVisible = false;
        knob = new ListScrollKnob { HorizontalAlignment = HorizontalAlignment.Right, Width = 6, Margin = new Thickness(0, 0, 2, 0) };
        var list = new Panel { Children = { rows, knob } };
        var stack = new DockPanel { Margin = new Thickness(0, 4) };
        DockPanel.SetDock(detail, Dock.Bottom);
        stack.Children.Add(detail);
        stack.Children.Add(list);
        return stack;
    }

    private static double MeasureMono(string text, double size) =>
        new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Preferences.EditorFontFamily), size, Brushes.Black).Width;

    private static (string Letter, DynamicColor Color) Symbol(CompletionKind kind) => kind switch
    {
        CompletionKind.Class => ("C", SyntaxColors.Type),
        CompletionKind.Struct => ("S", SyntaxColors.Type),
        CompletionKind.Interface => ("I", SyntaxColors.Interface),
        CompletionKind.Enum => ("E", SyntaxColors.Type),
        CompletionKind.EnumMember or CompletionKind.Constant => ("#", SyntaxColors.Member),
        CompletionKind.Delegate => ("D", SyntaxColors.Type),
        CompletionKind.Method or CompletionKind.ExtensionMethod => ("M", SyntaxColors.Method),
        CompletionKind.Property => ("P", SyntaxColors.Member),
        CompletionKind.Field => ("F", SyntaxColors.Member),
        CompletionKind.Event => ("bolt", SyntaxColors.Member),
        CompletionKind.Local or CompletionKind.Parameter => ("V", SyntaxColors.Parameter),
        CompletionKind.Namespace => ("{}", SyntaxColors.Namespace),
        CompletionKind.TypeParameter => ("T", SyntaxColors.Type),
        CompletionKind.Keyword => ("K", SyntaxColors.Keyword),
        CompletionKind.Snippet => ("+", Palette.Accent),
        _ => (string.Empty, Palette.SecondaryLabel),
    };

    private void Scroll(int delta)
    {
        _first = Math.Max(0, Math.Min(Math.Max(0, Items.Count - VisibleRows), _first + delta));
        Rebuild();
    }

    private void ScrollToVisible(int row)
    {
        if (row < _first)
        {
            _first = row;
        }
        else if (row >= _first + VisibleRows)
        {
            _first = row - VisibleRows + 1;
        }
    }

    /// <summary>Recreates the visible rows (at most ten; the list itself can hold 400 items).</summary>
    private void Rebuild()
    {
        _rows.Children.Clear();
        for (var index = _first; index < Math.Min(Items.Count, _first + VisibleRows); index++)
        {
            _rows.Children.Add(Row(Items[index], index));
        }

        _knob.Update(_first, Math.Min(VisibleRows, Items.Count), Items.Count);
    }

    private Control Row(CompletionItem item, int index)
    {
        var (letter, color) = Symbol(item.Kind);
        var icon = EditorGlyphs.LetterSquare(letter, color.Current, 13);
        icon.Margin = new Thickness(8, 0, 7, 0);
        var label = new TextBlock
        {
            Text = item.DisplayText,
            FontFamily = new FontFamily(Preferences.EditorFontFamily),
            FontSize = 12,
            Foreground = Palette.Label.Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var detail = new TextBlock
        {
            Text = item.InlineDescription ?? string.Empty,
            FontSize = 11,
            Foreground = Palette.TertiaryLabel.Brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(12, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new DockPanel
        {
            Height = RowHeight,
            Background = index == _selected ? Palette.RowSelection.Brush : Brushes.Transparent,
            Margin = new Thickness(4, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(detail, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(detail);
        row.Children.Add(label);
        row.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount >= 2 && Selected is { } selected && index == _selected)
            {
                OnCommit?.Invoke(selected);
                return;
            }

            Move(index - _selected);
        };
        return row;
    }

    /// <summary>The overlay scroller of the list (thin knob, shown when the list is longer than ten rows).</summary>
    private sealed class ListScrollKnob : Control
    {
        private (int First, int Visible, int Total) _state;

        public ListScrollKnob() => IsHitTestVisible = false;

        public void Update(int first, int visible, int total)
        {
            _state = (first, visible, total);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var (first, visible, total) = _state;
            if (total <= visible || total == 0)
            {
                return;
            }

            var height = Bounds.Height * visible / total;
            var top = Bounds.Height * first / total;
            context.DrawRectangle(Palette.TertiaryLabel.Brush, null, new Rect(0, top, Bounds.Width, Math.Max(12, height)), 3, 3);
        }
    }
}

/// <summary>Signature help and hover information as a small floating card.</summary>
public sealed class InfoPopup : PopupPanel
{
    private readonly ContentControl _host;

    public InfoPopup()
        : this(new ContentControl())
    {
    }

    private InfoPopup(ContentControl host)
        : base(host) => _host = host;

    public void Show(Control content, Control target, Rect anchor, bool above)
    {
        content.Margin = new Thickness(10, 7);
        content.MaxWidth = 620;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        _host.Content = content;
        _host.MinWidth = 160;
        _host.MaxWidth = 640;
        Show(target, anchor, null, above);
    }
}

/// <summary>Signature help: the active overload with the current parameter in bold accent colour.</summary>
public static class SignatureHelpCard
{
    public static Control Create(SignatureHelp help)
    {
        var signature = help.Signatures[Math.Min(help.ActiveSignature, help.Signatures.Count - 1)];
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (help.Signatures.Count > 1)
        {
            line.Children.Add(new Border
            {
                Background = Palette.QuaternaryFill.Brush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = $"{help.ActiveSignature + 1}/{help.Signatures.Count}", FontSize = 10, Foreground = Palette.SecondaryLabel.Brush },
            });
        }

        line.Children.Add(SignatureText(signature, help.ActiveParameter));
        var card = new StackPanel { Spacing = 4, Children = { line } };
        if (!string.IsNullOrEmpty(signature.Documentation))
        {
            card.Children.Add(new TextBlock { Text = signature.Documentation, FontSize = 10.5, Foreground = Palette.SecondaryLabel.Brush, TextWrapping = TextWrapping.Wrap });
        }

        return card;
    }

    private static TextBlock SignatureText(SignatureInfo signature, int activeParameter)
    {
        var text = new TextBlock { FontFamily = new FontFamily(Preferences.EditorFontFamily), FontSize = 12, Foreground = Palette.Label.Brush, TextWrapping = TextWrapping.Wrap };
        var inlines = new InlineCollection { new Run(signature.Prefix) };
        for (var index = 0; index < signature.Parameters.Count; index++)
        {
            if (index > 0)
            {
                inlines.Add(new Run(", "));
            }

            var part = new Run(signature.Parameters[index]);
            if (index == activeParameter)
            {
                part.FontWeight = FontWeight.Bold;
                part.Foreground = Palette.Accent.Brush;
            }

            inlines.Add(part);
        }

        inlines.Add(new Run(signature.Suffix));
        text.Inlines = inlines;
        return text;
    }
}

/// <summary>Hover card: diagnostics at the position, the symbol's signature and its documentation.</summary>
public static class QuickInfoCard
{
    public static Control Create(string signature, string? documentation, IReadOnlyList<Diagnostic> diagnostics)
    {
        var card = new StackPanel { Spacing = 6 };
        foreach (var diagnostic in diagnostics.Take(4))
        {
            var symbol = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "xmark.octagon.fill",
                DiagnosticSeverity.Warning => "exclamationmark.triangle.fill",
                _ => "info.circle.fill",
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(EditorGlyphs.Icon(symbol, Palette.ColorFor(diagnostic.Severity).Current, 12));
            row.Children.Add(new TextBlock { Text = diagnostic.Message, FontSize = 12, Foreground = Palette.Label.Brush, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            row.Children.Add(new TextBlock
            {
                Text = diagnostic.Code,
                FontSize = 10,
                FontFamily = new FontFamily(Preferences.EditorFontFamily),
                Foreground = Palette.SecondaryLabel.Brush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            card.Children.Add(row);
        }

        if (diagnostics.Count > 0 && signature.Length > 0)
        {
            card.Children.Add(new Border { Height = 1, Background = Palette.Separator.Brush });
        }

        if (signature.Length > 0)
        {
            card.Children.Add(new SelectableTextBlock { Text = signature, FontFamily = new FontFamily(Preferences.EditorFontFamily), FontSize = 12, Foreground = Palette.Label.Brush, TextWrapping = TextWrapping.Wrap });
        }

        if (!string.IsNullOrEmpty(documentation))
        {
            card.Children.Add(new TextBlock { Text = documentation, FontSize = 12, Foreground = Palette.SecondaryLabel.Brush, TextWrapping = TextWrapping.Wrap });
        }

        return card;
    }
}
