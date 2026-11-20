using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VsLike.Core.Code;

namespace VsLike.Editor.Intelligence;

/// <summary>
/// Zeichnet Wellenlinien unter Diagnosen. Die Marker sind Textsegmente und wandern bei Eingaben mit,
/// bis neue Diagnosen eintreffen.
/// </summary>
public sealed class DiagnosticMarkers : IBackgroundRenderer
{
    private readonly TextSegmentCollection<Marker> _markers;
    private readonly TextView _textView;

    public DiagnosticMarkers(TextView textView, TextDocument document)
    {
        _textView = textView;
        _markers = new TextSegmentCollection<Marker>(document);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyDictionary<DiagnosticSeverityKind, IBrush> Brushes { get; set; } = new Dictionary<DiagnosticSeverityKind, IBrush>
    {
        [DiagnosticSeverityKind.Error] = new SolidColorBrush(Color.Parse("#F07167")),
        [DiagnosticSeverityKind.Warning] = new SolidColorBrush(Color.Parse("#E9C46A")),
        [DiagnosticSeverityKind.Info] = new SolidColorBrush(Color.Parse("#6CA6F0")),
    };

    public int Count => _markers.Count;

    public void Set(IReadOnlyList<DiagnosticInfo> diagnostics, int documentLength)
    {
        _markers.Clear();
        foreach (var diagnostic in diagnostics.Where(d => d.Severity != DiagnosticSeverityKind.Hidden))
        {
            var start = Math.Clamp(diagnostic.Start, 0, documentLength);
            var length = Math.Clamp(Math.Max(diagnostic.Length, 1), 0, documentLength - start);
            _markers.Add(new Marker(diagnostic) { StartOffset = start, Length = length });
        }

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>Returns the diagnostics whose marker covers <paramref name="offset"/>.</summary>
    public IReadOnlyList<DiagnosticInfo> At(int offset)
        => [.. _markers.FindSegmentsContaining(offset).Select(m => m.Diagnostic)];

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _markers.Count == 0)
        {
            return;
        }

        var visual = textView.VisualLines;
        var viewStart = visual[0].FirstDocumentLine.Offset;
        var viewEnd = visual[^1].LastDocumentLine.EndOffset;
        foreach (var marker in _markers.FindOverlappingSegments(viewStart, viewEnd - viewStart).OrderBy(m => m.Diagnostic.Severity))
        {
            var brush = Brushes.GetValueOrDefault(marker.Diagnostic.Severity) ?? Avalonia.Media.Brushes.Gray;
            var pen = new Pen(brush, 1);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker))
            {
                drawingContext.DrawGeometry(null, pen, CreateWave(rect.BottomLeft, rect.Width));
            }
        }
    }

    private static StreamGeometry CreateWave(Point start, double width)
    {
        const double step = 2.5;
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(start.X, start.Y - 1), isFilled: false);
        var up = true;
        for (var x = start.X + step; x <= start.X + Math.Max(width, step * 2); x += step)
        {
            context.LineTo(new Point(x, start.Y - (up ? 3 : 1)));
            up = !up;
        }

        context.EndFigure(isClosed: false);
        return geometry;
    }

    private sealed class Marker(DiagnosticInfo diagnostic) : TextSegment
    {
        public DiagnosticInfo Diagnostic { get; } = diagnostic;
    }
}
