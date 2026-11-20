using Avalonia.Controls;
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
using VsLike.Core.Code;
using VsLike.Editor.Editing;
using VsLike.Editor.Folding;
using VsLike.Editor.Highlighting;
using VsLike.Editor.Intelligence;
using VsLike.Editor.Snippets;

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
    private readonly Debugging.ExecutionLineRenderer _executionLine = new();
    private CancellationTokenSource? _foldingCancellation;
    private SnippetSession? _snippet;

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
        Completion = new CompletionController(editor);
        SignatureHelp = new SignatureHelpController(editor);
        Diagnostics = new DiagnosticMarkers(editor.TextArea.TextView, editor.Document);
        editor.TextArea.TextView.BackgroundRenderers.Add(Diagnostics);
        BreakpointMargin = new Debugging.BreakpointMargin();
        editor.TextArea.LeftMargins.Insert(0, BreakpointMargin);
        editor.TextArea.TextView.BackgroundRenderers.Add(_executionLine);
        Semantic = new SemanticColorizer();
        editor.TextArea.TextView.LineTransformers.Add(Semantic);
        editor.TextArea.TextView.PointerHover += OnPointerHover;
        editor.TextArea.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        editor.TextArea.TextView.PointerHoverStopped += OnPointerHoverStopped;
        editor.TextArea.TextView.ScrollOffsetChanged += OnViewportChanged;
        _searchPanel = SearchPanel.Install(editor);
        _foldingManager = FoldingManager.Install(editor.TextArea);
        _foldingTimer = new DispatcherTimer { Interval = FoldingDelay };
        _foldingTimer.Tick += (_, _) =>
        {
            _foldingTimer.Stop();
            _ = UpdateFoldingsAsync();
        };

        editor.TextArea.TextEntering += OnTextEntering;
        editor.TextArea.TextEntered += OnTextEntered;
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.Document.TextChanged += OnTextChanged;
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        SetTheme(theme);
        _ = UpdateFoldingsAsync();
    }

    /// <summary>Raised when the caret moves; arguments are 1-based line and column.</summary>
    public event EventHandler<(int Line, int Column)>? CaretMoved;

    /// <summary>Raised on ⌘-click (Ctrl-click on Linux) with the clicked offset (Go to Definition).</summary>
    public event EventHandler<int>? DefinitionRequested;

    /// <summary>Raised after a single character was typed; arguments are the character and the caret offset after it.</summary>
    public event EventHandler<(char Typed, int Offset)>? CharTyped;

    /// <summary>Raised when the pointer rests over a text offset; listeners answer with <see cref="ShowQuickInfo"/>.</summary>
    public event EventHandler<int>? HoverRequested;

    /// <summary>Raised (debounced by the caller) when the visible text range changes.</summary>
    public event EventHandler? ViewportChanged;

    public EditorLanguage Language { get; }

    public DiagnosticMarkers Diagnostics { get; }

    public Debugging.BreakpointMargin BreakpointMargin { get; }

    public CompletionController Completion { get; }

    public SignatureHelpController SignatureHelp { get; }

    /// <summary>Gets or sets the resolver for the enclosing type name (used by snippets such as ctor).</summary>
    public Func<int, Task<string?>>? TypeNameResolver { get; set; }

    public bool IsSnippetActive => _snippet?.IsActive == true;

    public SemanticColorizer Semantic { get; }

    /// <summary>Gets the offsets of the first and last visible character (whole document if not yet laid out).</summary>
    public (int Start, int End) VisibleRange
    {
        get
        {
            var view = _editor.TextArea.TextView;
            if (!view.VisualLinesValid || view.VisualLines.Count == 0)
            {
                return (0, _editor.Document.TextLength);
            }

            return (view.VisualLines[0].FirstDocumentLine.Offset, view.VisualLines[^1].LastDocumentLine.EndOffset);
        }
    }

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
        Semantic.SetTheme(kind);
        _editor.TextArea.TextView.Redraw();
        _editor.TextArea.SelectionBrush = Brush(_textMate, "editor.selectionBackground", dark ? "#E2A63B40" : "#B4741A33");
        var caret = Brush(_textMate, "editorCursor.foreground", dark ? "#E2A63B" : "#9C5F0C");
        _editor.TextArea.Caret.CaretBrush = caret;
        MultiCaret.SetColors(caret, _editor.TextArea.SelectionBrush);
        _searchPanel.SetSearchResultsBrush(new SolidColorBrush(Color.Parse(dark ? "#553FB5A6" : "#401F8A7E")));
    }

    /// <summary>Shows or clears the line where the debugger is stopped and scrolls it into view.</summary>
    public void SetExecutionLine(int? line)
    {
        _executionLine.Line = line;
        BreakpointMargin.SetExecutionLine(line);
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        if (line is { } number && number >= 1 && number <= _editor.Document.LineCount)
        {
            _editor.ScrollTo(number, 1);
        }
    }

    public void SetDiagnostics(IReadOnlyList<DiagnosticInfo> diagnostics)
        => Diagnostics.Set(diagnostics, _editor.Document.TextLength);

    /// <summary>Applies semantic spans computed for <paramref name="version"/>; stale results are ignored.</summary>
    public bool ApplyClassifications(IReadOnlyList<ClassifiedSpan> spans, ITextSourceVersion? version)
    {
        if (!Semantic.SetSpans(spans, version, _editor.Document.Version))
        {
            return false;
        }

        _editor.TextArea.TextView.Redraw();
        return true;
    }

    /// <summary>Shows hover content; diagnostics at the hovered offset are always included.</summary>
    public void ShowQuickInfo(QuickInfo? info, int offset)
    {
        var font = _editor.FontFamily;
        var content = QuickInfoPresenter.Create(info, Diagnostics.At(offset), font);
        var view = _editor.TextArea.TextView;
        if (content is null)
        {
            ToolTip.SetIsOpen(view, false);
            return;
        }

        ToolTip.SetTip(view, content);
        ToolTip.SetPlacement(view, PlacementMode.Pointer);
        ToolTip.SetIsOpen(view, true);
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

    /// <summary>Replaces <paramref name="length"/> characters at <paramref name="start"/> with an expanded snippet.</summary>
    public async Task InsertSnippetAsync(SnippetDefinition snippet, int start, int length)
    {
        var document = _editor.Document;
        var line = document.GetLineByOffset(start);
        var lineText = document.GetText(line.Offset, line.Length);
        var indentation = lineText[..(lineText.Length - lineText.TrimStart().Length)];
        var typeName = TypeNameResolver is null ? null : await TypeNameResolver(start).ConfigureAwait(true);
        var expanded = SnippetExpander.Expand(snippet.Body, indentation, typeName);
        _snippet = SnippetSession.Insert(_editor, start, length, expanded);
        _editor.TextArea.Focus();
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
        _editor.TextArea.TextView.PointerHover -= OnPointerHover;
        _editor.TextArea.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _editor.TextArea.TextView.PointerHoverStopped -= OnPointerHoverStopped;
        _editor.TextArea.TextView.ScrollOffsetChanged -= OnViewportChanged;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(Diagnostics);
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_executionLine);
        _editor.TextArea.LeftMargins.Remove(BreakpointMargin);
        _editor.TextArea.TextView.LineTransformers.Remove(Semantic);
        ToolTip.SetIsOpen(_editor.TextArea.TextView, false);
        _editor.TextArea.TextEntering -= OnTextEntering;
        _editor.TextArea.TextEntered -= OnTextEntered;
        Completion.Close();
        SignatureHelp.Close();
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

    private void OnViewportChanged(object? sender, EventArgs e) => ViewportChanged?.Invoke(this, EventArgs.Empty);

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        var view = _editor.TextArea.TextView;
        if (view.GetPosition(e.GetPosition(view) + view.ScrollOffset) is not { } position)
        {
            return;
        }

        var offset = _editor.Document.GetOffset(position.Location);
        if (HoverRequested is null)
        {
            ShowQuickInfo(null, offset);
        }
        else
        {
            HoverRequested.Invoke(this, offset);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (DefinitionRequested is null || !e.KeyModifiers.HasFlag(primary))
        {
            return;
        }

        var view = _editor.TextArea.TextView;
        if (view.GetPosition(e.GetPosition(view) + view.ScrollOffset) is { } position)
        {
            DefinitionRequested.Invoke(this, _editor.Document.GetOffset(position.Location));
            e.Handled = true;
        }
    }

    private void OnPointerHoverStopped(object? sender, PointerEventArgs e) => ToolTip.SetIsOpen(_editor.TextArea.TextView, false);

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
        if (e.Text is { Length: 1 } single)
        {
            Completion.OnCharacterTyping(single[0]);
        }

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

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is { Length: 1 } single)
        {
            CharTyped?.Invoke(this, (single[0], _editor.CaretOffset));
        }
    }

    private bool TryHandleSnippetKey(KeyEventArgs e)
    {
        if (Completion.IsOpen || e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            _snippet?.End();
            return false;
        }

        if (e.Key != Key.Tab)
        {
            return false;
        }

        if (_snippet is { IsActive: true } active)
        {
            active.Next();
            return true;
        }

        if (Language != LanguageRegistry.CSharp || !_editor.TextArea.Selection.IsEmpty)
        {
            return false;
        }

        var caret = _editor.CaretOffset;
        var (start, end) = MultiCaretController.WordAt(_editor.Document, caret);
        if (end != caret || SnippetLibrary.Find(_editor.Document.GetText(start, end - start)) is not { } snippet)
        {
            return false;
        }

        _ = InsertSnippetAsync(snippet, start, end - start);
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && TryHandleSnippetKey(e))
        {
            e.Handled = true;
            return;
        }

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
