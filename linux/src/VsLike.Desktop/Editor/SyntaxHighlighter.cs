using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>
/// Colours a document: the lexer runs synchronously after every edit (no delay while typing); Roslyn's semantic
/// classification for the visible range follows debounced and is kept, shifted, across edits so that colours do not
/// flicker. Colours are applied by a line transformer at render time (no undo, no effect on the text), the counterpart
/// of the layout manager's temporary attributes on macOS.
/// </summary>
public sealed partial class SyntaxHighlighter
{
    private static readonly string[] MarkupExtensions = ["xml", "csproj", "props", "targets", "slnx", "config", "resx", "xaml", "axaml", "json"];

    private readonly CodeTextView _textView;
    private readonly CodeDocument _document;
    private readonly Workspace _workspace;
    private IReadOnlyList<ClassifiedSpan> _semantic = [];
    private IReadOnlyList<(int Start, int Length, DynamicColor Color)> _markup = [];
    private CancellationTokenSource? _semanticTask;

    public SyntaxHighlighter(CodeTextView textView, CodeDocument document, Workspace workspace)
    {
        _textView = textView;
        _document = document;
        _workspace = workspace;
        Colorizer = new LineColorizer(this);
    }

    public IReadOnlyList<Token> Tokens { get; private set; } = [];

    /// <summary>Gets the text the tokens were computed for (the UTF-16 units of the document).</summary>
    public string Units { get; private set; } = string.Empty;

    /// <summary>Gets the transformer that the text view runs for every visible line.</summary>
    public DocumentColorizingTransformer Colorizer { get; }

    /// <summary>Re-colours after an edit at <paramref name="editedRange"/> that changed the length by <paramref name="delta"/>.</summary>
    public void TextChanged(TextRange? editedRange, int delta)
    {
        if (editedRange is { } range)
        {
            ShiftSemantic(range, delta);
        }

        Recolor();
        ScheduleSemantic();
    }

    public void Recolor()
    {
        Units = _document.Text;
        Tokens = [];
        _markup = [];
        if (!_document.IsCSharp)
        {
            if (MarkupExtensions.Contains(System.IO.Path.GetExtension(_document.Path).TrimStart('.').ToLowerInvariant()))
            {
                _markup = ColorMarkup(Units);
            }

            _textView.TextArea.TextView.Redraw();
            return;
        }

        if (Units.Length < 3_000_000)
        {
            Tokens = CSharpLexer.Tokenize(Units);
        }

        _textView.TextArea.TextView.Redraw();
    }

    public void ScheduleSemantic()
    {
        _semanticTask?.Cancel();
        if (!_document.IsCSharp)
        {
            return;
        }

        var cancellation = _semanticTask = new CancellationTokenSource();
        _ = EditorTasks.Run(async () =>
        {
            await Task.Delay(220, cancellation.Token);
            await LoadSemantic(cancellation.Token);
        });
    }

    /// <summary>XML and JSON: tags, attribute names and strings.</summary>
    private static List<(int Start, int Length, DynamicColor Color)> ColorMarkup(string text)
    {
        var patterns = new (Regex Regex, DynamicColor Color)[]
        {
            (TagPattern(), SyntaxColors.Type),
            (AttributePattern(), SyntaxColors.Member),
            (StringPattern(), SyntaxColors.String),
            (CommentPattern(), SyntaxColors.Comment),
            (LiteralPattern(), SyntaxColors.Number),
        };
        var spans = new List<(int, int, DynamicColor)>();
        foreach (var (regex, color) in patterns)
        {
            spans.AddRange(regex.Matches(text).Select(m => (m.Index, m.Length, color)));
        }

        return spans;
    }

    private async Task LoadSemantic(CancellationToken cancellation)
    {
        if (_workspace.Engine is not { } engine || !_workspace.IsEngineReady || VisibleRange() is not { } range)
        {
            return;
        }

        _workspace.SyncNow(_document);
        var version = _document.Version;
        var spans = await engine.Classify(_document.Path, range.Location, range.Length, cancellation);
        if (cancellation.IsCancellationRequested || version != _document.Version)
        {
            return;
        }

        // Keep spans outside the visible range from earlier requests.
        _semantic = [.. _semantic.Where(s => s.Start + s.Length <= range.Location || s.Start >= range.End), .. spans];
        _textView.TextArea.TextView.Redraw();
    }

    /// <summary>Moves cached semantic spans after an edit and drops the ones it touched.</summary>
    private void ShiftSemantic(TextRange editedRange, int delta)
    {
        var oldEnd = editedRange.End - delta;
        _semantic =
        [
            .. _semantic.Select(span =>
            {
                if (span.Start + span.Length <= editedRange.Location)
                {
                    return span;
                }

                return span.Start >= oldEnd ? span with { Start = span.Start + delta } : null;
            }).OfType<ClassifiedSpan>()
        ];
    }

    /// <summary>The characters of the visible lines plus one screen above and below (like the Swift original).</summary>
    private TextRange? VisibleRange()
    {
        var view = _textView.TextArea.TextView;
        var height = view.Bounds.Height;
        if (height <= 0 || view.Document is not { } document || document.TextLength == 0)
        {
            return null;
        }

        var top = Math.Max(0, view.VerticalOffset - height);
        var bottom = Math.Min(view.DocumentHeight, view.VerticalOffset + (2 * height));
        var first = view.GetDocumentLineByVisualTop(top) ?? document.GetLineByNumber(1);
        var last = view.GetDocumentLineByVisualTop(bottom) ?? document.GetLineByNumber(document.LineCount);
        var length = last.EndOffset - first.Offset;
        return length > 0 ? new TextRange(first.Offset, length) : null;
    }

    private void ColorizeLine(DocumentLine line, Action<int, int, IBrush> paint)
    {
        var start = line.Offset;
        var end = line.EndOffset;
        if (end <= start || Units.Length != _document.Storage.TextLength)
        {
            return;
        }

        foreach (var (spanStart, spanLength, color) in _markup)
        {
            Paint(spanStart, spanLength, color.Brush);
        }

        var tokens = Tokens;
        for (var index = FirstTokenAtOrAfter(tokens, start); index < tokens.Count && tokens[index].Start < end; index++)
        {
            var token = tokens[index];
            if (token.Kind != TokenKind.Identifier)
            {
                Paint(token.Start, token.Length, SyntaxColors.ColorFor(token.Kind).Brush);
            }
        }

        foreach (var span in _semantic)
        {
            if (span.Start + span.Length > start && span.Start < end && span.Start + span.Length <= Units.Length && SyntaxColors.ColorFor(span.Classification) is { } color)
            {
                Paint(span.Start, span.Length, color.Brush);
            }
        }

        void Paint(int from, int length, IBrush brush)
        {
            var clippedStart = Math.Max(from, start);
            var clippedEnd = Math.Min(from + length, end);
            if (clippedEnd > clippedStart)
            {
                paint(clippedStart, clippedEnd, brush);
            }
        }
    }

    /// <summary>Index of the first token that ends after <paramref name="offset"/> (tokens are sorted and do not overlap).</summary>
    private static int FirstTokenAtOrAfter(IReadOnlyList<Token> tokens, int offset)
    {
        var low = 0;
        var high = tokens.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (tokens[mid].Start + tokens[mid].Length <= offset)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    [GeneratedRegex(@"</?[A-Za-z_][\w.:-]*|/?>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\b[A-Za-z_][\w.:-]*(?==)")]
    private static partial Regex AttributePattern();

    [GeneratedRegex("\"[^\"\\n]*\"")]
    private static partial Regex StringPattern();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\b(true|false|null)\b|-?\b\d+(\.\d+)?\b")]
    private static partial Regex LiteralPattern();

    /// <summary>Applies the colours of <see cref="ColorizeLine"/> to the visual line being built.</summary>
    private sealed class LineColorizer(SyntaxHighlighter owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line) =>
            owner.ColorizeLine(line, (start, end, brush) => ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush)));
    }
}
