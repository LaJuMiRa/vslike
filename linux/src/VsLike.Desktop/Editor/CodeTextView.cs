using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Search;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>
/// The code editor: an AvaloniaEdit <see cref="TextEditor"/> (NSTextView with TextKit 1 on macOS) with C# editing
/// behaviour, IntelliSense popups, quick actions (Ctrl+Shift+.), snippets, breakpoints and diagnostics. The control is
/// the editor together with its scroll-bar marks and popups, the counterpart of the (scroll view, text view) pair.
/// </summary>
public sealed partial class CodeTextView : Grid, ICodeEditor
{
    private readonly MarkerScroller _markerScroller = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly SquiggleLayoutManager _squiggles = new();
    private readonly LineBackgroundRenderer _lineBackgrounds;
    private int _lineCount = 1;
    private (TextRange Range, int Delta)? _lastEdit;
    private bool _refreshScheduled;

    // NSTextView's `stillSelecting`: a mouse drag is extending the selection.
    private bool _isMouseSelecting;
    private CancellationTokenSource? _completionTask;
    private CancellationTokenSource? _signatureTask;
    private CancellationTokenSource? _hoverTask;
    private CancellationTokenSource? _detailTask;

    private CodeTextView(CodeDocument document, Workspace workspace)
    {
        Document = document;
        Workspace = workspace;
        Editor = new TextEditor { Document = document.Storage };
        _lineBackgrounds = new LineBackgroundRenderer(this);
        Highlighter = new SyntaxHighlighter(this, document, workspace);
        Gutter = new LineGutter(this);
        Children.Add(Editor);
        Children.Add(_markerScroller);
        Children.Add(Completion.Host);
        Children.Add(SignaturePopup.Host);
        Children.Add(HoverPopup.Host);
    }

    public CodeDocument? Document { get; }

    /// <summary>
    /// Detaches the editor from its document when the tab is closed (Swift: removes the layout manager from the text
    /// storage): stops pending engine requests and hides the popups.
    /// </summary>
    public void Detach()
    {
        foreach (var task in new[] { _completionTask, _signatureTask, _hoverTask, _detailTask })
        {
            task?.Cancel();
        }

        Completion.Dismiss();
        SignaturePopup.Dismiss();
        HoverPopup.Dismiss();
        Editor.Document = null;
    }

    public Workspace Workspace { get; }

    /// <summary>Gets the AvaloniaEdit control showing <see cref="CodeDocument.Storage"/>.</summary>
    public TextEditor Editor { get; }

    public TextArea TextArea => Editor.TextArea;

    public SyntaxHighlighter? Highlighter { get; }

    public LineGutter Gutter { get; }

    public CompletionPopup Completion { get; } = new();

    public InfoPopup SignaturePopup { get; } = new();

    public InfoPopup HoverPopup { get; } = new();

    public CompletionList? CompletionList { get; set; }

    public int CompletionStart { get; set; }

    public SnippetSession? Snippet { get; set; }

    public TextRange? BracketHighlight { get; private set; }

    public IReadOnlyDictionary<int, DiagnosticSeverity> DiagnosticSeverityByLine { get; private set; } = new Dictionary<int, DiagnosticSeverity>();

    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

    /// <summary>Gets the font size of the text (from the preferences).</summary>
    public double EditorFontSize => Editor.FontSize;

    /// <summary>Gets the text of the document (the UTF-16 units).</summary>
    public string Text => Editor.Document.Text;

    public int LineCount => _lineCount;

    public int CaretLine => LineNumber(SelectedRange.Location);

    public TextRange SelectedRange => new(Editor.SelectionStart, Editor.SelectionLength);

    public static CodeTextView Make(CodeDocument document, Workspace workspace)
    {
        var view = new CodeTextView(document, workspace);
        view.Configure();
        view.RebuildLineStarts();
        view.Highlighter?.Recolor();
        view.Highlighter?.ScheduleSemantic();
        return view;
    }

    public void Configure()
    {
        Editor.Options = new TextEditorOptions
        {
            ConvertTabsToSpaces = true,
            IndentationSize = TextEditing.IndentUnit.Length,
            HighlightCurrentLine = false,
            EnableHyperlinks = false,
            EnableEmailHyperlinks = false,
            AllowScrollBelowDocument = false,
            EnableRectangularSelection = true,
            CutCopyWholeLine = false,
            LineHeightFactor = 1.18,
        };
        Editor.ShowLineNumbers = false;
        Editor.WordWrap = false;
        Editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        TextArea.RightClickMovesCaret = true;
        TextArea.SelectionCornerRadius = 0;
        TextArea.SelectionBorder = null;
        TextArea.SelectionForeground = null;

        // textContainerInset (6, 8) of the NSTextView: the gutter and the text start 8 points below the top.
        TextArea.TextView.Margin = new Thickness(6, 8, 0, 0);
        Gutter.Margin = new Thickness(0, 8, 0, 0);
        TextArea.LeftMargins.Insert(0, Gutter);
        TextArea.TextView.LineTransformers.Add(Highlighter!.Colorizer);
        TextArea.TextView.BackgroundRenderers.Add(_lineBackgrounds);
        TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        SearchPanel.Install(Editor);
        ApplyColors();
        ApplyFont();

        Gutter.OnToggleBreakpoint = ToggleBreakpoint;
        Gutter.OnEditCondition = EditBreakpointCondition;
        Completion.OnCommit = item => CommitCompletion(item);
        Completion.OnSelectionChange = LoadCompletionDetail;

        var storage = Editor.Document;
        storage.Changing += (_, e) => Snippet?.TextWillChange(new TextRange(e.Offset, e.RemovalLength), e.InsertedText.Text);
        storage.Changed += (_, e) => StorageEdited(new TextRange(e.Offset, e.InsertionLength), e.InsertionLength - e.RemovalLength);
        TextArea.Caret.PositionChanged += (_, _) => SelectionDidChange();
        TextArea.SelectionChanged += (_, _) => SelectionDidChange();
        TextArea.TextView.ScrollOffsetChanged += (_, _) => Scrolled();
        TextArea.TextEntering += OnTextEntering;
        TextArea.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        TextArea.AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        TextArea.TextView.PointerMoved += OnTextPointerMoved;
        TextArea.TextView.PointerExited += (_, _) => MouseExited();
        TextArea.GotFocus += (_, _) => BecameFirstResponder();
        TextArea.LostFocus += (_, _) => ResignedFirstResponder();
        TextArea.ContextMenu = new ContextMenu();
        TextArea.ContextMenu.Opening += (_, _) => FillContextMenu(TextArea.ContextMenu);
        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) => ApplyColors();
        }
    }

    /// <summary>Applies the light or dark palette (NSColor dynamic providers resolve themselves on macOS).</summary>
    public void ApplyColors()
    {
        Editor.Background = Palette.EditorBackground.Brush;
        Editor.Foreground = SyntaxColors.Text.Brush;
        TextArea.SelectionBrush = Palette.Selection.Brush;
        TextArea.CaretBrush = Palette.Accent.Brush;
        Highlighter?.Recolor();
        Gutter.InvalidateVisual();
        SetDiagnostics(Diagnostics);
    }

    public void ApplyFont()
    {
        var size = Workspace.Preferences.FontSize;
        var family = new FontFamily(Preferences.EditorFontFamily);
        if (Math.Abs(Editor.FontSize - size) < 0.01 && Editor.FontFamily == family)
        {
            return;
        }

        Editor.FontFamily = family;
        Editor.FontSize = size;
        Gutter.UpdateThickness(_lineCount);
        Gutter.InvalidateVisual();
    }

    // MARK: Lines
    public void RebuildLineStarts()
    {
        _lineCount = Editor.Document.LineCount;
        Gutter.UpdateThickness(_lineCount);
    }

    /// <summary>1-based line of a UTF-16 offset.</summary>
    public int LineNumber(int offset) => Editor.Document.GetLineByOffset(Math.Clamp(offset, 0, Editor.Document.TextLength)).LineNumber;

    public int OffsetOfLine(int line) => Editor.Document.GetLineByNumber(Math.Clamp(line, 1, Editor.Document.LineCount)).Offset;

    // MARK: Text changes
    /// <summary>Called for every character change of the storage (typing, undo, code fixes, reloads).</summary>
    public void StorageEdited(TextRange range, int delta)
    {
        TextStorageEdited(range);
        _lastEdit = _lastEdit is { } previous
            ? (Union(previous.Range, range), previous.Delta + delta)
            : (range, delta);
        if (_refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshScheduled = false;
            DocumentEdited();
        });
    }

    public void TextStorageEdited(TextRange range)
    {
        var oldLineCount = _lineCount;
        var editLine = LineNumber(range.Location);
        RebuildLineStarts();
        var lineDelta = _lineCount - oldLineCount;
        if (lineDelta == 0 || Document is not { } document || document.Breakpoints.Count == 0)
        {
            return;
        }

        var moved = new Dictionary<int, SourceBreakpoint>();
        foreach (var (line, breakpoint) in document.Breakpoints)
        {
            if (line > editLine || (line == editLine && range.Location == OffsetOfLine(line) && lineDelta > 0))
            {
                var target = line + lineDelta;
                if (target > editLine || lineDelta > 0)
                {
                    moved[Math.Max(1, target)] = new SourceBreakpoint(Math.Max(1, target), breakpoint.Condition);
                }
            }
            else
            {
                moved[line] = breakpoint;
            }
        }

        document.Breakpoints = moved;
        Workspace.Debugger.BreakpointsChanged(document);
    }

    public void DocumentEdited()
    {
        var edit = _lastEdit;
        _lastEdit = null;
        Highlighter?.TextChanged(edit?.Range, edit?.Delta ?? 0);
        UpdateBracketMatch();
        TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        Gutter.InvalidateVisual();
    }

    // MARK: Diagnostics
    public void SetDiagnostics(IReadOnlyList<Diagnostic> list)
    {
        if (Document is not { } document)
        {
            return;
        }

        Diagnostics = list;
        var length = Editor.Document.TextLength;
        var squiggles = new List<Squiggle>();
        var byLine = new Dictionary<int, DiagnosticSeverity>();
        var marks = new List<MarkerScroller.Mark>();
        var total = (double)Math.Max(1, LineCount);
        foreach (var diagnostic in list.Where(d => d.Severity != DiagnosticSeverity.Hidden))
        {
            var start = diagnostic.Start;
            var count = diagnostic.Length;
            if (diagnostic.Category == "Build" || (start == 0 && count == 0 && diagnostic.StartLine > 1))
            {
                start = document.Offset(diagnostic.StartLine, diagnostic.StartColumn);
                count = 0;
            }

            if (count == 0)
            {
                // Underline the word at the position so that zero-length diagnostics are visible.
                var word = WordRange(start);
                start = word.Location;
                count = Math.Max(1, word.Length);
            }

            var location = Math.Min(start, length);
            var clipped = Math.Max(0, Math.Min(count, length - location));
            var color = diagnostic.IsStyleCop ? Palette.StyleCop : Palette.ColorFor(diagnostic.Severity);
            if (clipped > 0)
            {
                squiggles.Add(new Squiggle(location, clipped, color.Current, diagnostic.Severity == DiagnosticSeverity.Error));
            }

            var line = diagnostic.StartLine;
            byLine[line] = byLine.TryGetValue(line, out var existing) && existing > diagnostic.Severity ? existing : diagnostic.Severity;
            if (diagnostic.Severity >= DiagnosticSeverity.Warning)
            {
                marks.Add(new MarkerScroller.Mark((line - 1) / total, color.Current));
            }
        }

        marks.AddRange(document.Breakpoints.Keys.Select(line => new MarkerScroller.Mark((line - 1) / total, Palette.Breakpoint.Current)));
        DiagnosticSeverityByLine = byLine;
        _squiggles.Squiggles = squiggles;
        _markerScroller.Marks = Workspace.Preferences.ShowMinimapOfDiagnostics ? marks : [];
        TextArea.TextView.InvalidateLayer(_squiggles.Layer);
        Gutter.InvalidateVisual();
    }

    public TextRange WordRange(int offset)
    {
        var text = Text;
        if (offset >= text.Length)
        {
            return new TextRange(Math.Max(0, text.Length - 1), text.Length > 0 ? 1 : 0);
        }

        var start = Math.Max(0, offset);
        var end = start;
        while (start > 0 && CSharpLexer.IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && CSharpLexer.IsIdentifierPart(text[end]))
        {
            end++;
        }

        return new TextRange(start, end - start);
    }

    // MARK: Selection
    public void SetSelectedRange(TextRange range)
    {
        var length = Editor.Document.TextLength;
        var location = Math.Clamp(range.Location, 0, length);
        Editor.Select(location, Math.Clamp(range.Length, 0, length - location));
    }

    public void UpdateBracketMatch()
    {
        if (Highlighter is not { } highlighter)
        {
            return;
        }

        var previous = BracketHighlight;
        BracketHighlight = null;
        _lineBackgrounds.Brackets = [];
        var caret = SelectedRange.Location;
        var units = highlighter.Units;
        foreach (var candidate in new[] { caret - 1, caret })
        {
            if (candidate < 0 || candidate >= units.Length || TextEditing.MatchingBracket(candidate, units, highlighter.Tokens) is not { } match)
            {
                continue;
            }

            _lineBackgrounds.Brackets = [candidate, match];
            BracketHighlight = new TextRange(Math.Min(candidate, match), Math.Abs(match - candidate) + 1);
            break;
        }

        if (previous != BracketHighlight)
        {
            TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        }
    }

    private static TextRange Union(TextRange a, TextRange b)
    {
        var start = Math.Min(a.Location, b.Location);
        return new TextRange(start, Math.Max(a.End, b.End) - start);
    }

    private void SelectionDidChange()
    {
        TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        Gutter.InvalidateVisual();
        if (_isMouseSelecting)
        {
            return;
        }

        UpdateBracketMatch();
        Snippet?.SelectionChanged(SelectedRange);
        if (Completion.IsVisible && (SelectedRange.Location < CompletionStart || SelectedRange.Length > 0))
        {
            DismissCompletion();
        }

        // Parameter info follows the caret and closes once it leaves the argument list.
        if (SignaturePopup.IsVisible)
        {
            RefreshSignatureHelp();
        }
    }

    private void Scrolled()
    {
        Highlighter?.ScheduleSemantic();
        if (Completion.IsVisible || SignaturePopup.IsVisible)
        {
            PositionPopups();
        }

        HoverPopup.Dismiss();
        Gutter.InvalidateVisual();
    }

    private void BecameFirstResponder()
    {
        Workspace.ActiveEditor = this;
        TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    private void ResignedFirstResponder()
    {
        DismissCompletion();
        DismissSignatureHelp();
        HoverPopup.Dismiss();
        TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }
}
