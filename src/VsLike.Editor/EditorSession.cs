using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using VsLike.Editor.Editing;
using VsLike.Editor.Folding;
using VsLike.Editor.Highlighting;

namespace VsLike.Editor;

/// <summary>
/// Verbindet ein AvaloniaEdit-<see cref="TextEditor"/> mit den VsLike-Funktionen: TextMate-Highlighting, Folding,
/// Suchen/Ersetzen, Auto-Klammern, Kommentieren und Mehrfach-Cursor.
/// </summary>
public sealed class EditorSession : IDisposable
{
    private static readonly TimeSpan FoldingDelay = TimeSpan.FromMilliseconds(400);

    private readonly TextEditor _editor;
    private readonly TextMate.Installation? _textMate;
    private readonly FoldingManager _foldingManager;
    private readonly XmlFoldingStrategy _xmlFolding = new();
    private readonly SearchPanel _searchPanel;
    private readonly DispatcherTimer _foldingTimer;
    private CancellationTokenSource? _foldingCancellation;

    public EditorSession(TextEditor editor, string? filePath, EditorThemeKind theme)
    {
        _editor = editor;
        Language = LanguageRegistry.FromPath(filePath);

        editor.ShowLineNumbers = true;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.EnableRectangularSelection = true;
        editor.Options.CutCopyWholeLine = true;
        if (Language == LanguageRegistry.CSharp)
        {
            editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(editor.Options);
        }

        if (Language.TextMateScope is { } scope)
        {
            _textMate = editor.InstallTextMate(new VsLikeRegistryOptions());
            _textMate.SetGrammar(scope);
        }

        MultiCaret = new MultiCaretController(editor);
        _searchPanel = SearchPanel.Install(editor);
        _foldingManager = FoldingManager.Install(editor.TextArea);
        _foldingTimer = new DispatcherTimer { Interval = FoldingDelay };
        _foldingTimer.Tick += (_, _) =>
        {
            _foldingTimer.Stop();
            _ = UpdateFoldingsAsync();
        };

        editor.TextArea.TextEntering += OnTextEntering;
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.Document.TextChanged += OnTextChanged;
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        SetTheme(theme);
        _ = UpdateFoldingsAsync();
    }

    /// <summary>Raised when the caret moves; arguments are 1-based line and column.</summary>
    public event EventHandler<(int Line, int Column)>? CaretMoved;

    public EditorLanguage Language { get; }

    public TextEditor Editor => _editor;

    public (int Line, int Column) Caret => (_editor.TextArea.Caret.Line, _editor.TextArea.Caret.Column);

    public MultiCaretController MultiCaret { get; }

    public SearchPanel SearchPanel => _searchPanel;

    internal bool AutoBracketsEnabled => Language == LanguageRegistry.CSharp || Language == LanguageRegistry.Json;

    public void SetTheme(EditorThemeKind kind)
    {
        _textMate?.SetTheme(VsLikeRegistryOptions.GetTheme(kind));
        var dark = kind == EditorThemeKind.Dark;
        _editor.Background = Brush(_textMate, "editor.background", dark ? "#191B21" : "#FFFFFF");
        _editor.Foreground = Brush(_textMate, "editor.foreground", dark ? "#DCE1E8" : "#1E2228");
        _editor.LineNumbersForeground = Brush(_textMate, "editorLineNumber.foreground", dark ? "#5E6675" : "#9AA0AA");
        _editor.TextArea.TextView.CurrentLineBackground = Brush(_textMate, "editor.lineHighlightBackground", dark ? "#20232B" : "#F4F4F0");
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
        _editor.TextArea.SelectionBrush = Brush(_textMate, "editor.selectionBackground", dark ? "#E2A63B40" : "#B4741A33");
        var caret = Brush(_textMate, "editorCursor.foreground", dark ? "#E2A63B" : "#9C5F0C");
        _editor.TextArea.Caret.CaretBrush = caret;
        MultiCaret.SetColors(caret, _editor.TextArea.SelectionBrush);
        _searchPanel.SetSearchResultsBrush(new SolidColorBrush(Color.Parse(dark ? "#553FB5A6" : "#401F8A7E")));
    }

    public void OpenFind()
    {
        _searchPanel.IsReplaceMode = false;
        _searchPanel.Open();
    }

    public void OpenReplace()
    {
        _searchPanel.IsReplaceMode = true;
        _searchPanel.Open();
    }

    /// <summary>Toggles line or block comments for the selected lines (or the caret line).</summary>
    public void ToggleComment()
    {
        var document = _editor.Document;
        var area = _editor.TextArea;
        var startOffset = area.Selection.IsEmpty ? _editor.CaretOffset : area.Selection.SurroundingSegment.Offset;
        var endOffset = area.Selection.IsEmpty ? _editor.CaretOffset : area.Selection.SurroundingSegment.EndOffset;
        var firstLine = document.GetLineByOffset(startOffset);
        var lastLine = document.GetLineByOffset(endOffset);
        if (lastLine.LineNumber > firstLine.LineNumber && endOffset == lastLine.Offset)
        {
            lastLine = lastLine.PreviousLine;
        }

        var segmentStart = firstLine.Offset;
        var segmentEnd = lastLine.EndOffset;
        var text = document.GetText(segmentStart, segmentEnd - segmentStart);
        string replacement;
        if (Language.LineComment is { } token)
        {
            var lines = text.Split('\n');
            replacement = string.Join('\n', CommentToggler.ToggleLineComments(lines, token));
        }
        else if (Language.BlockComment is { } block)
        {
            replacement = CommentToggler.ToggleBlockComment(text, block.Start, block.End);
        }
        else
        {
            return;
        }

        document.Replace(segmentStart, segmentEnd - segmentStart, replacement);
        _editor.Select(segmentStart, replacement.Length);
    }

    /// <summary>Moves the caret to a 1-based line/column and scrolls it into view.</summary>
    public void GoTo(int line, int column = 1)
    {
        var document = _editor.Document;
        var safeLine = Math.Clamp(line, 1, document.LineCount);
        var docLine = document.GetLineByNumber(safeLine);
        var offset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
        _editor.TextArea.ClearSelection();
        _editor.CaretOffset = offset;
        _editor.ScrollTo(safeLine, column);
        _editor.TextArea.Focus();
    }

    public void Dispose()
    {
        _foldingTimer.Stop();
        _foldingCancellation?.Cancel();
        _editor.Document.TextChanged -= OnTextChanged;
        _editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        _editor.TextArea.TextEntering -= OnTextEntering;
        _editor.TextArea.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        MultiCaret.Dispose();
        _searchPanel.Uninstall();
        FoldingManager.Uninstall(_foldingManager);
        _textMate?.Dispose();
    }

    private static IBrush Brush(TextMate.Installation? installation, string key, string fallback)
        => installation is not null && installation.TryGetThemeColor(key, out var color) && color is not null
            ? new SolidColorBrush(Color.Parse(color))
            : new SolidColorBrush(Color.Parse(fallback));

    private static char? CharAt(TextDocument document, int offset)
        => offset >= 0 && offset < document.TextLength ? document.GetCharAt(offset) : null;

    private void OnCaretPositionChanged(object? sender, EventArgs e) => CaretMoved?.Invoke(this, Caret);

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    private async Task UpdateFoldingsAsync()
    {
        _foldingCancellation?.Cancel();
        var cancellation = _foldingCancellation = new CancellationTokenSource();
        var document = _editor.Document;
        if (Language == LanguageRegistry.Xml)
        {
            _xmlFolding.UpdateFoldings(_foldingManager, document);
            return;
        }

        if (Language != LanguageRegistry.CSharp && Language != LanguageRegistry.Json)
        {
            return;
        }

        var snapshot = document.CreateSnapshot();
        var foldings = await Task.Run(() => BraceFoldingStrategy.Compute(snapshot.Text), cancellation.Token).ConfigureAwait(true);
        if (!cancellation.IsCancellationRequested && foldings.All(f => f.EndOffset <= document.TextLength))
        {
            _foldingManager.UpdateFoldings(foldings, -1);
        }
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Handled || !AutoBracketsEnabled || e.Text is not { Length: 1 } typedText || !_editor.TextArea.Selection.IsEmpty)
        {
            return;
        }

        var typed = typedText[0];
        var document = _editor.Document;
        var caret = _editor.CaretOffset;
        var next = CharAt(document, caret);
        if (BracketPairing.ShouldOvertype(typed, next))
        {
            _editor.CaretOffset = caret + 1;
            e.Handled = true;
        }
        else if (BracketPairing.ShouldAutoClose(typed, CharAt(document, caret - 1), next) && BracketPairing.ClosingFor(typed) is { } closing)
        {
            document.Insert(caret, $"{typed}{closing}");
            _editor.CaretOffset = caret + 1;
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Back || !AutoBracketsEnabled || !_editor.TextArea.Selection.IsEmpty || MultiCaret.ExtraCaretCount > 0)
        {
            return;
        }

        var caret = _editor.CaretOffset;
        var document = _editor.Document;
        if (BracketPairing.ShouldDeletePair(CharAt(document, caret - 1), CharAt(document, caret)))
        {
            document.Remove(caret - 1, 2);
            e.Handled = true;
        }
    }
}
