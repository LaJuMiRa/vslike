using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VsLike.Desktop.Editor;

namespace VsLike.Desktop;

/// <summary>
/// A fast read-only log (LogView in BottomPanel.swift): appends new lines incrementally, highlights errors and warnings
/// and follows the end of the log while it is scrolled to the bottom.
/// </summary>
public sealed class LogView : UserControl
{
    private readonly TextEditor _editor;
    private readonly List<LineKind> _kinds = [];
    private IReadOnlyList<int> _shownIds = [];

    public LogView()
    {
        _editor = new TextEditor
        {
            IsReadOnly = true,
            FontFamily = Ui.Mono,
            FontSize = 11.5,
            ShowLineNumbers = false,
            Padding = new Thickness(8, 6),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Document = new TextDocument(),
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.TextArea.TextView.LineTransformers.Add(new LineColorizer(_kinds));
        _editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        Content = _editor;
        ApplyColors();
        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) =>
            {
                ApplyColors();
                _editor.TextArea.TextView.Redraw();
            };
        }
    }

    private enum LineKind
    {
        Normal,
        Error,
        Warning,
        Success,
    }

    /// <summary>Shows <paramref name="lines"/>; lines that extend the shown ones are appended.</summary>
    public void Update(IReadOnlyList<OutputLog.Line> lines)
    {
        var ids = lines.Select(l => l.Id).ToList();
        var isAppend = _shownIds.Count <= ids.Count && ids.Take(_shownIds.Count).SequenceEqual(_shownIds);
        var view = _editor.TextArea.TextView;
        var wasAtBottom = view.ScrollOffset.Y + view.Bounds.Height >= view.DocumentHeight - 30;
        var newLines = isAppend ? lines.Skip(_shownIds.Count).ToList() : lines;
        if (!isAppend)
        {
            _editor.Document.Text = string.Empty;
            _kinds.Clear();
        }

        _shownIds = ids;
        if (newLines.Count == 0)
        {
            return;
        }

        foreach (var line in newLines)
        {
            _kinds.Add(Classify(line));
        }

        var chunk = string.Concat(newLines.Select(l => l.Text + "\n"));
        _editor.Document.Insert(_editor.Document.TextLength, chunk);
        if (wasAtBottom || !isAppend)
        {
            _editor.ScrollToEnd();
        }
    }

    private static LineKind Classify(OutputLog.Line line)
    {
        var lower = line.Text.ToLowerInvariant();
        if (line.IsError || lower.Contains(": error ", StringComparison.Ordinal) || lower.Contains("error:", StringComparison.Ordinal))
        {
            return LineKind.Error;
        }

        if (lower.Contains(": warning ", StringComparison.Ordinal))
        {
            return LineKind.Warning;
        }

        return lower.Contains("succeeded", StringComparison.Ordinal) || lower.Contains("passed!", StringComparison.Ordinal) || lower.Contains("bestanden", StringComparison.Ordinal)
            ? LineKind.Success
            : LineKind.Normal;
    }

    private void ApplyColors()
    {
        _editor.Background = Palette.EditorBackground.Brush;
        _editor.Foreground = SyntaxColors.Text.Brush;
    }

    private sealed class LineColorizer : DocumentColorizingTransformer
    {
        private readonly List<LineKind> _kinds;

        public LineColorizer(List<LineKind> kinds)
        {
            _kinds = kinds;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;
            if (index < 0 || index >= _kinds.Count || line.Length == 0)
            {
                return;
            }

            var color = _kinds[index] switch
            {
                LineKind.Error => Palette.Error,
                LineKind.Warning => Palette.Warning,
                LineKind.Success => Palette.Success,
                _ => null,
            };
            if (color is not null)
            {
                var brush = color.Brush;
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }
}
