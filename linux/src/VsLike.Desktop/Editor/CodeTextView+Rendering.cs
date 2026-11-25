using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>Background drawing of the code editor (NSTextView's <c>drawBackground(in:)</c> and temporary background attributes).</summary>
public sealed partial class CodeTextView
{
    /// <summary>
    /// Execution line, current line (only with an empty selection and keyboard focus), matching brackets and the brief
    /// find indicator after navigation.
    /// </summary>
    private sealed class LineBackgroundRenderer(CodeTextView owner) : IBackgroundRenderer
    {
        private TextRange? _indicator;
        private IDisposable? _indicatorTimer;

        public IReadOnlyList<int> Brackets { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Background;

        public void ShowFindIndicator(TextRange range)
        {
            _indicator = range.Length > 0 ? range : null;
            _indicatorTimer?.Dispose();
            _indicatorTimer = DispatcherTimer.RunOnce(
                () =>
                {
                    _indicator = null;
                    owner.TextArea.TextView.InvalidateLayer(Layer);
                },
                TimeSpan.FromMilliseconds(900));
            owner.TextArea.TextView.InvalidateLayer(Layer);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner.Document is not { } document || !textView.VisualLinesValid)
            {
                return;
            }

            if (document.ExecutionLine is { } line)
            {
                FillLine(textView, drawingContext, line, Palette.ExecutionLine.Brush);
            }

            if (owner.SelectedRange.Length == 0 && owner.TextArea.IsKeyboardFocusWithin)
            {
                FillLine(textView, drawingContext, owner.CaretLine, Palette.CurrentLine.Brush);
            }

            foreach (var offset in Brackets.Where(o => o < textView.Document.TextLength))
            {
                Fill(textView, drawingContext, new TextSegment { StartOffset = offset, Length = 1 }, Palette.BracketMatch.Brush);
            }

            if (_indicator is { } indicator && indicator.End <= textView.Document.TextLength)
            {
                Fill(textView, drawingContext, new TextSegment { StartOffset = indicator.Location, Length = indicator.Length }, new SolidColorBrush(Palette.WithAlpha(Palette.Accent.Current, 0.45)));
            }
        }

        private static void FillLine(TextView textView, DrawingContext context, int line, IBrush brush)
        {
            if (line < 1 || line > textView.Document.LineCount || textView.GetVisualLine(line) is not { } visual)
            {
                return;
            }

            var top = visual.VisualTop - textView.VerticalOffset;
            context.FillRectangle(brush, new Rect(-textView.Margin.Left, top, textView.Bounds.Width + textView.Margin.Left, visual.Height));
        }

        private static void Fill(TextView textView, DrawingContext context, TextSegment segment, IBrush brush)
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, false))
            {
                context.FillRectangle(brush, rect);
            }
        }
    }
}
