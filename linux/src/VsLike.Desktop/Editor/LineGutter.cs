using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>
/// Line numbers, breakpoints (click to toggle, right-click for a condition), the execution arrow and diagnostic
/// marks to the left of the text (an NSRulerView on macOS, a left margin of AvaloniaEdit here).
/// </summary>
public sealed class LineGutter : AbstractMargin
{
    private readonly CodeTextView _codeView;
    private double _ruleThickness = 52;

    public LineGutter(CodeTextView textView)
    {
        _codeView = textView;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public Action<int>? OnToggleBreakpoint { get; set; }

    public Action<int>? OnEditCondition { get; set; }

    private double FontSize => Math.Max(10, _codeView.EditorFontSize - 2);

    public void UpdateThickness(int lineCount)
    {
        var digits = Math.Max(3, lineCount.ToString(CultureInfo.InvariantCulture).Length);
        var width = (digits * Measure("8", FontSize, FontWeight.Normal).Width) + 34;
        if (Math.Abs(_ruleThickness - width) > 0.5)
        {
            _ruleThickness = width;
            InvalidateMeasure();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Palette.EditorBackground.Brush, new Rect(Bounds.Size));
        if (TextView is not { VisualLinesValid: true } view || _codeView.Document is null)
        {
            return;
        }

        var caretLine = _codeView.CaretLine;
        var diagnosticLines = _codeView.DiagnosticSeverityByLine;
        foreach (var line in view.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var y = line.VisualTop - view.VerticalOffset;
            diagnosticLines.TryGetValue(number, out var severity);
            DrawLine(context, number, y, line.Height, number == caretLine, diagnosticLines.ContainsKey(number) ? severity : null);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(_ruleThickness, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= VisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += VisualLinesChanged;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (LineAt(e.GetPosition(this)) is not { } line)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnEditCondition?.Invoke(line);
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            OnToggleBreakpoint?.Invoke(line);
        }

        e.Handled = true;
    }

    private static FormattedText Measure(string text, double size, FontWeight weight, IBrush? brush = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush ?? Brushes.Black);

    private void VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void DrawLine(DrawingContext context, int line, double y, double height, bool isCaret, DiagnosticSeverity? severity)
    {
        var document = _codeView.Document;
        var markerSize = Math.Min(height - 4, 13);
        var markerRect = new Rect(6, y + ((height - markerSize) / 2), markerSize + 8, markerSize);
        if (document is not null && document.Breakpoints.TryGetValue(line, out var breakpoint))
        {
            var radius = markerSize / 2.6;
            context.DrawRectangle(Palette.Breakpoint.Brush, null, markerRect, radius, radius);
            if (!string.IsNullOrEmpty(breakpoint.Condition))
            {
                context.DrawEllipse(Brushes.White, null, markerRect.Center, 2, 2);
            }
        }
        else if (severity is { } level && level >= DiagnosticSeverity.Warning)
        {
            var color = Palette.WithAlpha(Palette.ColorFor(level).Current, 0.9);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(13, y + (height / 2)), 3, 3);
        }

        if (document?.ExecutionLine == line)
        {
            var top = y + 2;
            var bottom = y + height - 2;
            var arrow = new StreamGeometry();
            using (var path = arrow.Open())
            {
                path.BeginFigure(new Point(4, top), true);
                path.LineTo(new Point(_ruleThickness - 14, top));
                path.LineTo(new Point(_ruleThickness - 6, (top + bottom) / 2));
                path.LineTo(new Point(_ruleThickness - 14, bottom));
                path.LineTo(new Point(4, bottom));
                path.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(Palette.WithAlpha(Palette.Warning.Current, 0.85)), null, arrow);
        }

        var brush = isCaret ? Palette.GutterActive.Brush : Palette.GutterText.Brush;
        var text = Measure(line.ToString(CultureInfo.InvariantCulture), FontSize, isCaret ? FontWeight.SemiBold : FontWeight.Normal, brush);
        context.DrawText(text, new Point(_ruleThickness - text.Width - 10, y + ((height - text.Height) / 2)));
    }

    private int? LineAt(Point point)
    {
        if (TextView is not { } view || view.Document is null)
        {
            return null;
        }

        var line = view.GetDocumentLineByVisualTop(point.Y + view.VerticalOffset);
        return line?.LineNumber ?? view.Document.LineCount;
    }
}

/// <summary>The marks for errors, warnings and breakpoints over the whole file, drawn over the vertical scroll bar.</summary>
public sealed class MarkerScroller : Control
{
    private IReadOnlyList<Mark> _marks = [];

    public MarkerScroller()
    {
        IsHitTestVisible = false;
        Width = 11;
    }

    public IReadOnlyList<Mark> Marks
    {
        get
        {
            return _marks;
        }

        set
        {
            _marks = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var track = new Rect(Bounds.Size).Deflate(new Thickness(0, 4));
        foreach (var mark in _marks)
        {
            var y = track.Top + (mark.Fraction * track.Height);
            context.DrawRectangle(new SolidColorBrush(mark.Color), null, new Rect(track.Left + 2, y - 1.5, Math.Max(4, track.Width - 4), 3), 1.5, 1.5);
        }
    }

    public readonly record struct Mark(double Fraction, Color Color);
}
