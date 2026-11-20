using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace VsLike.Editor.Debugging;

/// <summary>Hinterlegt die Zeile, an der der Debugger gerade steht.</summary>
public sealed class ExecutionLineRenderer : IBackgroundRenderer
{
    public int? Line { get; set; }

    public IBrush Brush { get; set; } = new SolidColorBrush(Color.FromArgb(0x40, 0xE2, 0xA6, 0x3B));

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Line is not { } line || textView.Document is null || line < 1 || line > textView.Document.LineCount)
        {
            return;
        }

        var documentLine = textView.Document.GetLineByNumber(line);
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, documentLine))
        {
            drawingContext.FillRectangle(Brush, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
        }
    }
}
