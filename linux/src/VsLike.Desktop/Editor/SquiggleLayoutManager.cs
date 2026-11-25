using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VsLike.Desktop.Editor;

/// <summary>A diagnostic underline: characters, colour and whether it is thick (errors).</summary>
public readonly record struct Squiggle(int Start, int Length, Color Color, bool IsThick);

/// <summary>
/// Draws diagnostic underlines as wavy lines under exactly the affected characters (SquiggleLayoutManager on macOS,
/// which draws the underline temporary attributes). <see cref="CodeTextView.SetDiagnostics"/> fills <see cref="Squiggles"/>.
/// </summary>
public sealed class SquiggleLayoutManager : IBackgroundRenderer
{
    private const double Amplitude = 1.6;
    private const double Wavelength = 4.5;

    public IReadOnlyList<Squiggle> Squiggles { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is not { } document || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
        {
            return;
        }

        var firstVisible = textView.VisualLines[0].FirstDocumentLine.Offset;
        var lastVisible = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var squiggle in Squiggles)
        {
            if (squiggle.Start + squiggle.Length < firstVisible || squiggle.Start > lastVisible || squiggle.Start + squiggle.Length > document.TextLength)
            {
                continue;
            }

            var segment = new TextSegment { StartOffset = squiggle.Start, Length = squiggle.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, false))
            {
                var baseline = Baseline(textView, rect);
                DrawWave(drawingContext, rect.Left, rect.Right, rect.Top + baseline + Amplitude + 1.5, squiggle);
            }
        }
    }

    private static double Baseline(TextView textView, Rect rect)
    {
        var line = textView.GetVisualLineFromVisualTop(rect.Top + textView.ScrollOffset.Y + 1);
        return line?.TextLines.Count > 0 ? line.TextLines[0].Baseline : textView.DefaultBaseline;
    }

    private static void DrawWave(DrawingContext context, double minX, double right, double y, Squiggle squiggle)
    {
        var maxX = Math.Max(right, minX + Wavelength);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(minX, y), false);
            var x = minX;
            var up = true;
            const double half = Wavelength / 2;
            while (x < maxX)
            {
                var next = Math.Min(x + half, maxX);
                var offset = (up ? -Amplitude : Amplitude) * 1.3;
                path.CubicBezierTo(new Point(x + (half / 2), y + offset), new Point(next - (half / 2), y + offset), new Point(next, y));
                x = next;
                up = !up;
            }

            path.EndFigure(false);
        }

        var pen = new Pen(new SolidColorBrush(squiggle.Color), squiggle.IsThick ? 1.5 : 1.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, geometry);
    }
}
