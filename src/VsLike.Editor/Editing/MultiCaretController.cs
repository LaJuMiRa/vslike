using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace VsLike.Editor.Editing;

/// <summary>
/// Zusätzliche Cursor neben dem Haupt-Cursor: Alt+Klick fügt einen Cursor hinzu, ⌘D/Ctrl+D markiert das nächste
/// Vorkommen der Auswahl. Tippen, Backspace und Entf wirken auf alle Cursor als ein Undo-Schritt. Esc oder
/// Cursorbewegung ohne Mehrfachauswahl beenden den Modus.
/// </summary>
public sealed class MultiCaretController : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private readonly List<(TextAnchor Start, TextAnchor End)> _extra = [];
    private IBrush _caretBrush = Brushes.Orange;
    private IBrush _selectionBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xE2, 0xA6, 0x3B));

    public MultiCaretController(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
        editor.TextArea.TextEntering += OnTextEntering;
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.TextArea.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    public KnownLayer Layer => KnownLayer.Caret;

    public int ExtraCaretCount => _extra.Count;

    public IReadOnlyList<(int Start, int End)> ExtraRanges => [.. _extra.Select(r => (r.Start.Offset, r.End.Offset))];

    public void SetColors(IBrush caret, IBrush selection)
    {
        _caretBrush = caret;
        _selectionBrush = selection;
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void AddCaret(int offset) => AddRange(offset, offset);

    /// <summary>Selects the word at the caret, or adds the next occurrence of the current selection.</summary>
    public void AddNextOccurrence()
    {
        var area = _editor.TextArea;
        var document = _editor.Document;
        if (area.Selection.IsEmpty)
        {
            var (start, end) = WordAt(document, _editor.CaretOffset);
            if (end > start)
            {
                _editor.Select(start, end - start);
            }

            return;
        }

        var segment = area.Selection.SurroundingSegment;
        var needle = document.GetText(segment);
        var searchFrom = _extra.Count > 0 ? _extra.Max(r => r.End.Offset) : segment.EndOffset;
        var index = document.Text.IndexOf(needle, searchFrom, StringComparison.Ordinal);
        if (index < 0)
        {
            index = document.Text.IndexOf(needle, 0, StringComparison.Ordinal);
        }

        if (index >= 0 && index != segment.Offset && _extra.All(r => r.Start.Offset != index))
        {
            AddRange(index, index + needle.Length);
        }
    }

    public void Clear()
    {
        _extra.Clear();
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        foreach (var (start, end) in _extra)
        {
            if (end.Offset > start.Offset)
            {
                var segment = new TextSegment { StartOffset = start.Offset, EndOffset = end.Offset };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.FillRectangle(_selectionBrush, rect);
                }
            }

            var location = _editor.Document.GetLocation(end.Offset);
            var position = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineTop) - textView.ScrollOffset;
            drawingContext.FillRectangle(_caretBrush, new Rect(position.X, position.Y, 2, textView.DefaultLineHeight));
        }
    }

    public void Dispose()
    {
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
        _editor.TextArea.TextEntering -= OnTextEntering;
        _editor.TextArea.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _editor.TextArea.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
    }

    internal static (int Start, int End) WordAt(TextDocument document, int offset)
    {
        var text = document.Text;
        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>Applies an edit to the primary selection and all extra ranges in one undo group.</summary>
    internal void ApplyToAll(Func<int, int, (int Start, int End, string Text)> edit)
    {
        var document = _editor.Document;
        var area = _editor.TextArea;
        var primary = area.Selection.IsEmpty
            ? (Start: _editor.CaretOffset, End: _editor.CaretOffset)
            : (Start: area.Selection.SurroundingSegment.Offset, End: area.Selection.SurroundingSegment.EndOffset);
        var ranges = _extra.Select(r => (Start: r.Start.Offset, End: r.End.Offset, IsPrimary: false))
            .Append((primary.Start, primary.End, IsPrimary: true))
            .OrderByDescending(r => r.Start)
            .ToList();

        TextAnchor? primaryAnchor = null;
        document.BeginUpdate();
        try
        {
            foreach (var range in ranges)
            {
                var (start, end, text) = edit(range.Start, range.End);
                document.Replace(start, end - start, text);
                if (range.IsPrimary)
                {
                    primaryAnchor = document.CreateAnchor(start + text.Length);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }

        var collapsed = _extra.Select(r => document.CreateAnchor(r.End.Offset)).ToList();
        _extra.Clear();
        foreach (var anchor in collapsed)
        {
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            _extra.Add((anchor, anchor));
        }

        area.ClearSelection();
        if (primaryAnchor is not null)
        {
            _editor.CaretOffset = primaryAnchor.Offset;
        }

        area.TextView.InvalidateLayer(Layer);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void AddRange(int start, int end)
    {
        var document = _editor.Document;
        var startAnchor = document.CreateAnchor(start);
        var endAnchor = document.CreateAnchor(end);
        startAnchor.MovementType = AnchorMovementType.BeforeInsertion;
        endAnchor.MovementType = AnchorMovementType.AfterInsertion;
        _extra.Add((startAnchor, endAnchor));
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_extra.Count == 0 || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var text = e.Text;
        ApplyToAll((start, end) => (start, end, text));
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_extra.Count == 0)
        {
            return;
        }

        var length = _editor.Document.TextLength;
        switch (e.Key)
        {
            case Key.Escape:
                Clear();
                e.Handled = true;
                break;
            case Key.Back:
                ApplyToAll((start, end) => start == end ? (Math.Max(0, start - 1), end, string.Empty) : (start, end, string.Empty));
                e.Handled = true;
                break;
            case Key.Delete:
                ApplyToAll((start, end) => start == end ? (start, Math.Min(length, end + 1), string.Empty) : (start, end, string.Empty));
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown:
                Clear();
                break;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                Clear();
            }

            return;
        }

        var textView = _editor.TextArea.TextView;
        var position = textView.GetPosition(e.GetPosition(textView) + textView.ScrollOffset);
        if (position is { } p)
        {
            AddCaret(_editor.Document.GetOffset(p.Location));
            e.Handled = true;
        }
    }
}
