using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace VsLike.Editor.Debugging;

/// <summary>
/// Leiste links neben den Zeilennummern: Breakpoints (Kreis, mit Bedingung als Raute, ungebunden hohl) und Pfeil der
/// aktuellen Ausführungszeile. Breakpoints hängen an <see cref="TextAnchor"/>s und wandern beim Bearbeiten mit.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 16;
    private readonly List<(TextAnchor Anchor, EditorBreakpoint Breakpoint)> _breakpoints = [];
    private int? _executionLine;

    public BreakpointMargin()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Raised with the 1-based line the user clicked (toggle).</summary>
    public event EventHandler<int>? ToggleRequested;

    /// <summary>Raised with the 1-based line the user right-clicked (edit condition).</summary>
    public event EventHandler<int>? EditRequested;

    /// <summary>Raised after edits moved breakpoints to other lines.</summary>
    public event EventHandler<IReadOnlyList<EditorBreakpoint>>? Moved;

    public static IBrush BreakpointBrush { get; set; } = new SolidColorBrush(Color.Parse("#E5484D"));

    public static IBrush ExecutionBrush { get; set; } = new SolidColorBrush(Color.Parse("#E2A63B"));

    public IReadOnlyList<EditorBreakpoint> Breakpoints => [.. _breakpoints.Where(b => !b.Anchor.IsDeleted).Select(b => b.Breakpoint with { Line = b.Anchor.Line })];

    public int? ExecutionLine => _executionLine;

    public void SetBreakpoints(IEnumerable<EditorBreakpoint> breakpoints)
    {
        _breakpoints.Clear();
        if (Document is { } document)
        {
            foreach (var breakpoint in breakpoints.Where(b => b.Line >= 1 && b.Line <= document.LineCount))
            {
                var anchor = document.CreateAnchor(document.GetLineByNumber(breakpoint.Line).Offset);
                anchor.SurviveDeletion = false;
                _breakpoints.Add((anchor, breakpoint));
            }
        }

        InvalidateVisual();
    }

    public void SetExecutionLine(int? line)
    {
        _executionLine = line;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid)
        {
            return;
        }

        foreach (var visualLine in view.VisualLines)
        {
            var number = visualLine.FirstDocumentLine.LineNumber;
            var top = visualLine.VisualTop - view.VerticalOffset;
            var center = new Point(MarginWidth / 2, top + (visualLine.Height / 2));
            var breakpoint = _breakpoints.FirstOrDefault(b => !b.Anchor.IsDeleted && b.Anchor.Line == number);
            if (breakpoint.Breakpoint is { } bp)
            {
                var pen = new Pen(BreakpointBrush, 1.5);
                var fill = bp.Verified ? BreakpointBrush : null;
                if (string.IsNullOrWhiteSpace(bp.Condition))
                {
                    context.DrawEllipse(fill, pen, center, 5, 5);
                }
                else
                {
                    var diamond = new StreamGeometry();
                    using (var g = diamond.Open())
                    {
                        g.BeginFigure(center + new Vector(0, -6), true);
                        g.LineTo(center + new Vector(6, 0));
                        g.LineTo(center + new Vector(0, 6));
                        g.LineTo(center + new Vector(-6, 0));
                        g.EndFigure(true);
                    }

                    context.DrawGeometry(fill, pen, diamond);
                }
            }

            if (_executionLine == number)
            {
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    g.BeginFigure(center + new Vector(-5, -5), true);
                    g.LineTo(center + new Vector(5, 0));
                    g.LineTo(center + new Vector(-5, 5));
                    g.EndFigure(true);
                }

                context.DrawGeometry(ExecutionBrush, null, arrow);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
    }

    protected override void OnDocumentChanged(TextDocument? oldDocument, TextDocument? newDocument)
    {
        if (oldDocument is not null)
        {
            oldDocument.Changed -= OnDocumentTextChanged;
        }

        base.OnDocumentChanged(oldDocument, newDocument);
        if (newDocument is not null)
        {
            newDocument.Changed += OnDocumentTextChanged;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TextView is not { } view || Document is null)
        {
            return;
        }

        var y = e.GetPosition(view).Y + view.VerticalOffset;
        var line = view.GetVisualLineFromVisualTop(y)?.FirstDocumentLine.LineNumber;
        if (line is not { } number)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            EditRequested?.Invoke(this, number);
        }
        else
        {
            ToggleRequested?.Invoke(this, number);
        }

        e.Handled = true;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnDocumentTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_breakpoints.Count == 0)
        {
            return;
        }

        var before = _breakpoints.Select(b => b.Breakpoint.Line).ToList();
        var removed = _breakpoints.RemoveAll(b => b.Anchor.IsDeleted);
        var moved = false;
        for (var i = 0; i < _breakpoints.Count; i++)
        {
            var (anchor, breakpoint) = _breakpoints[i];
            if (anchor.Line != breakpoint.Line)
            {
                _breakpoints[i] = (anchor, breakpoint with { Line = anchor.Line });
                moved = true;
            }
        }

        if (moved || removed > 0 || before.Count != _breakpoints.Count)
        {
            Moved?.Invoke(this, Breakpoints);
            InvalidateVisual();
        }
    }
}
