using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>Keyboard, typing, snippets, mouse and context menu of the code editor (the rest of CodeTextView.swift).</summary>
public sealed partial class CodeTextView
{
    private static readonly char[] TypeOverCharacters = [')', ']', '}', '"', '\''];

    // MARK: Keyboard
    /// <summary>
    /// Inserts typed text with the editor's rules (auto-closing pairs, typing over closers, outdenting <c>}</c>), the
    /// counterpart of NSTextView's <c>insertText(_:replacementRange:)</c>. A null range replaces the selection.
    /// </summary>
    public void InsertText(string text, TextRange? replacementRange = null)
    {
        var range = replacementRange ?? SelectedRange;
        if (text.Length != 1 && !(text.Length == 2 && char.IsSurrogatePair(text, 0)))
        {
            Replace(range, text);
            return;
        }

        var character = text[0];
        var current = Text;
        char? next = range.Location < current.Length ? current[range.Location] : null;

        // Type over an auto-inserted closing character.
        if (range.Length == 0 && TypeOverCharacters.Contains(character) && next == character)
        {
            SetSelectedRange(new TextRange(range.Location + 1, 0));
            AfterTyping(character);
            return;
        }

        if (Workspace.Preferences.AutoCloseBrackets && TextEditing.ClosingPair(character) is { } closing && ShouldAutoClose(character, next, range.Location))
        {
            if (range.Length > 0)
            {
                var selected = current.Substring(range.Location, range.Length);
                Replace(range, character + selected + closing);
                SetSelectedRange(new TextRange(range.Location + 1, selected.Length));
            }
            else
            {
                Replace(range, $"{character}{closing}");
                SetSelectedRange(new TextRange(range.Location + 1, 0));
            }

            AfterTyping(character);
            return;
        }

        // Outdent a closing brace typed on an otherwise empty line.
        if (character == '}')
        {
            var lineStart = TextEditing.LineRange(current, range.Location).Location;
            var before = current[lineStart..range.Location];
            if (before.Length >= TextEditing.IndentUnit.Length && before.All(c => c == ' '))
            {
                var unit = TextEditing.IndentUnit.Length;
                Replace(new TextRange(range.Location - unit, unit + range.Length), "}");
                AfterTyping(character);
                return;
            }
        }

        Replace(range, text);
        AfterTyping(character);
    }

    public void InsertTab()
    {
        if (Snippet is { } snippet && snippet.Next(this))
        {
            return;
        }

        Snippet = null;
        if (ExpandSnippetBeforeCaret())
        {
            return;
        }

        var selection = SelectedRange;
        if (selection.Length > 0 && Text.Substring(selection.Location, selection.Length).Contains('\n', StringComparison.Ordinal))
        {
            ShiftSelectedLines(1);
            return;
        }

        InsertText(TextEditing.IndentUnit, selection);
    }

    public void InsertBacktab()
    {
        if (Snippet is { } snippet && snippet.Previous(this))
        {
            return;
        }

        ShiftSelectedLines(-1);
    }

    public void InsertNewline()
    {
        Snippet = null;
        var text = Text;
        var selection = SelectedRange;
        if (selection.Length > 0)
        {
            var (newText, caret) = TextEditing.Newline(selection.Location, text);
            Replace(selection, newText);
            SetSelectedRange(new TextRange(selection.Location + caret, 0));
        }
        else
        {
            var (edit, caret) = TextEditing.NewlineEdit(TextEditing.NewlineLocation(selection.Location, text), text);
            Replace(new TextRange(edit.Start, edit.Length), edit.NewText);
            SetSelectedRange(new TextRange(caret, 0));
        }

        TextArea.Caret.BringCaretToView();
    }

    public void DeleteBackward()
    {
        var range = SelectedRange;
        var text = Text;
        if (range.Length == 0 && range.Location > 0)
        {
            // Delete a whole indent unit when the caret sits in leading whitespace.
            var lineStart = TextEditing.LineRange(text, range.Location).Location;
            var before = text[lineStart..range.Location];
            if (before.Length > 0 && before.All(c => c == ' '))
            {
                var remove = ((before.Length - 1) % TextEditing.IndentUnit.Length) + 1;
                Replace(new TextRange(range.Location - remove, remove), string.Empty);
                return;
            }

            // Remove an auto-inserted closing pair together with the opening one.
            if (range.Location < text.Length && TextEditing.ClosingPair(text[range.Location - 1]) is { } closing && text[range.Location] == closing)
            {
                Replace(new TextRange(range.Location - 1, 2), string.Empty);
                UpdateCompletionFilter();
                return;
            }
        }

        if (range.Length > 0)
        {
            Replace(range, string.Empty);
        }
        else if (range.Location > 0)
        {
            var width = range.Location > 1 && char.IsSurrogatePair(text[range.Location - 2], text[range.Location - 1]) ? 2 : 1;
            Replace(new TextRange(range.Location - width, width), string.Empty);
        }

        UpdateCompletionFilter();
        if (SignaturePopup.IsVisible)
        {
            RefreshSignatureHelp();
        }
    }

    public bool ShouldAutoClose(char character, char? next, int location)
    {
        if (next is { } following && (char.IsLetterOrDigit(following) || following == '_'))
        {
            return false;
        }

        if (character is '"' or '\'')
        {
            var tokens = Highlighter?.Tokens ?? [];
            if (tokens.Any(t => t.Kind is TokenKind.String or TokenKind.Character or TokenKind.Comment && location > t.Start && location < t.Start + t.Length))
            {
                return false;
            }

            if (location > 0 && char.IsLetterOrDigit(Text[location - 1]))
            {
                return false;
            }
        }

        return true;
    }

    public void AfterTyping(char character)
    {
        if (Document?.IsCSharp != true)
        {
            return;
        }

        if (Completion.IsVisible)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                UpdateCompletionFilter();
                return;
            }

            if (Completion.Selected is { } item && ".;(),[<> =".Contains(character, StringComparison.Ordinal) && CompletionList?.Items.Count > 0 && TypedPrefix.Length > 0)
            {
                // Commit characters: keep the typed character after the committed item.
                CommitCompletion(item, character);
                return;
            }

            DismissCompletion();
        }

        switch (character)
        {
            case '(' or ',':
                RefreshSignatureHelp();
                break;
            case ')':
                DismissSignatureHelp();
                break;
        }

        if (character is '.' or '<' or ' ' or '_' or '#' or '[' || char.IsLetter(character))
        {
            MaybeTriggerCompletion(character);
        }
    }

    public void ShiftSelectedLines(int direction)
    {
        var text = Text;
        var lines = TextEditing.LineRange(text, SelectedRange);
        var parts = text.Substring(lines.Location, lines.Length).Split('\n').ToList();
        var trailing = parts[^1].Length == 0;
        if (trailing)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        var shifted = parts.Select(line =>
        {
            if (direction > 0)
            {
                return line.Length == 0 ? line : TextEditing.IndentUnit + line;
            }

            var spaces = line.TakeWhile(c => c == ' ').Count();
            return line[Math.Min(spaces, TextEditing.IndentUnit.Length)..];
        });
        var replacement = string.Join("\n", shifted) + (trailing ? "\n" : string.Empty);
        Replace(lines, replacement);
        SetSelectedRange(new TextRange(lines.Location, replacement.Length - (trailing ? 1 : 0)));
    }

    public void ToggleComment()
    {
        var (range, text) = TextEditing.ToggleLineComment(Text, SelectedRange);
        Replace(range, text);
        SetSelectedRange(new TextRange(range.Location, Math.Max(0, text.Length - 1)));
    }

    // MARK: Snippets
    /// <summary>Expands the word before the caret when it is a snippet shortcut. Returns true when it did.</summary>
    public bool ExpandSnippetBeforeCaret()
    {
        var selection = SelectedRange;
        if (Document?.IsCSharp != true || selection.Length != 0)
        {
            return false;
        }

        var word = WordRange(Math.Max(0, selection.Location - 1));
        if (word.End != selection.Location || word.Length == 0 || VsLike.Kit.Snippet.Find(Text.Substring(word.Location, word.Length)) is not { } definition)
        {
            return false;
        }

        Expand(definition, word);
        return true;
    }

    public void Expand(VsLike.Kit.Snippet definition, TextRange range) => _ = EditorTasks.Run(async () =>
    {
        string? typeName = null;
        if (definition.Body.Contains("$TYPE", StringComparison.Ordinal) && Workspace.Engine is { } engine && Document is { } document)
        {
            Workspace.SyncNow(document);
            typeName = await engine.EnclosingType(document.Path, range.Location);
        }

        var indent = TextEditing.Indentation(range.Location, Text);
        var expanded = SnippetExpander.Expand(definition.Body, indent, typeName);
        Replace(range, expanded.Text);
        var session = new SnippetSession(expanded, range.Location);
        Snippet = session.Stops.Count == 0 ? null : session;
        SetSelectedRange(session.FirstSelection ?? new TextRange(range.Location + expanded.FinalCaret, 0));
    });

    /// <summary>An editing replacement (NSTextView's <c>super.insertText</c>): typing runs merge into one undo step.</summary>
    private void Replace(TextRange range, string text)
    {
        var storage = Editor.Document;
        var location = Math.Clamp(range.Location, 0, storage.TextLength);
        var length = Math.Clamp(range.Length, 0, storage.TextLength - location);
        storage.UndoStack.StartContinuedUndoGroup(this);
        try
        {
            storage.Replace(location, length, text);
        }
        finally
        {
            storage.UndoStack.EndUndoGroup();
        }

        Editor.Select(location + text.Length, 0);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || TextArea.IsReadOnly || e.Text.Any(char.IsControl))
        {
            return;
        }

        e.Handled = true;
        HoverPopup.Dismiss();
        InsertText(e.Text);
        TextArea.Caret.BringCaretToView();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        HoverPopup.Dismiss();
        var plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0;
        if (Completion.IsVisible && HandleCompletionKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (SignaturePopup.IsVisible)
            {
                DismissSignatureHelp();
                e.Handled = true;
                return;
            }

            if (Snippet is not null)
            {
                Snippet = null;
                e.Handled = true;
                return;
            }
        }

        if (!plain || TextArea.IsReadOnly)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                InsertBacktab();
                break;
            case Key.Tab:
                InsertTab();
                break;
            case Key.Enter:
                InsertNewline();
                break;
            case Key.Back when e.KeyModifiers == KeyModifiers.None:
                DeleteBackward();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private bool HandleCompletionKey(Key key)
    {
        switch (key)
        {
            case Key.Down:
                Completion.Move(1);
                return true;
            case Key.Up:
                Completion.Move(-1);
                return true;
            case Key.PageDown:
                Completion.Move(9);
                return true;
            case Key.PageUp:
                Completion.Move(-9);
                return true;
            case Key.Enter or Key.Tab when Completion.Selected is { } item:
                CommitCompletion(item);
                return true;
            case Key.Escape:
                DismissCompletion();
                return true;
            default:
                return false;
        }
    }

    // MARK: Mouse
    private void OnTextPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(TextArea.TextView);
        var token = EditorTasks.Renew(ref _hoverTask);
        if (CharacterOffset(point) is not { } offset)
        {
            HoverPopup.Dismiss();
            TextArea.TextView.Cursor = null;
            return;
        }

        TextArea.TextView.Cursor = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? new Cursor(StandardCursorType.Hand) : null;
        _ = EditorTasks.Run(async () =>
        {
            await Task.Delay(450, token);
            await ShowHover(offset, point, token);
        });
    }

    private void MouseExited()
    {
        _hoverTask?.Cancel();
        HoverPopup.Dismiss();
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HoverPopup.Dismiss();
        DismissCompletion();
        var point = e.GetCurrentPoint(TextArea.TextView);
        if (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && CharacterOffset(point.Position) is { } offset && Document is { } document)
        {
            SetSelectedRange(new TextRange(offset, 0));
            _ = EditorTasks.Run(() => Workspace.GoToDefinition(document, offset));
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _isMouseSelecting = true;
            e.Pointer.Capture(TextArea);
            TextArea.PointerReleased += EndMouseSelection;
        }
    }

    private void EndMouseSelection(object? sender, PointerReleasedEventArgs e)
    {
        TextArea.PointerReleased -= EndMouseSelection;
        _isMouseSelecting = false;
        SelectionDidChange();
    }

    private void FillContextMenu(ContextMenu menu)
    {
        var items = new List<Control>();
        void Item(string title, string symbol, KeyGesture? gesture, Action action)
        {
            var entry = new MenuItem { Header = title, Icon = EditorGlyphs.Icon(symbol, Palette.SecondaryLabel.Current, 13), InputGesture = gesture };
            entry.Click += (_, _) => action();
            items.Add(entry);
        }

        if (Document?.IsCSharp == true)
        {
            Item(L.T("Quick Actions and Fixes…"), "lightbulb", new KeyGesture(Key.OemPeriod, KeyModifiers.Control | KeyModifiers.Shift), ShowQuickActions);
            items.Add(new Separator());
            Item(L.T("Go to Definition"), "arrow.right.circle", null, GoToDefinition);
            Item(L.T("Find References"), "magnifyingglass", null, FindReferences);
            Item(L.T("Rename Symbol…"), "pencil", null, RenameSymbol);
            items.Add(new Separator());
            Item(L.T("Format Document"), "text.alignleft", null, () =>
            {
                if (Document is { } document)
                {
                    _ = EditorTasks.Run(() => Workspace.FormatDocument(document));
                }
            });
            Item(L.T("Toggle Comment"), "text.badge.minus", new KeyGesture(Key.OemQuestion, KeyModifiers.Control), ToggleComment);
            Item(L.T("Toggle Breakpoint"), "circle.fill", null, ToggleBreakpointAtCaret);
            items.Add(new Separator());
        }

        items.Add(Plain(L.T("Cut"), new KeyGesture(Key.X, KeyModifiers.Control), Editor.Cut));
        items.Add(Plain(L.T("Copy"), new KeyGesture(Key.C, KeyModifiers.Control), Editor.Copy));
        items.Add(Plain(L.T("Paste"), new KeyGesture(Key.V, KeyModifiers.Control), Editor.Paste));
        menu.ItemsSource = items;

        static MenuItem Plain(string title, KeyGesture gesture, Action action)
        {
            var entry = new MenuItem { Header = title, InputGesture = gesture };
            entry.Click += (_, _) => action();
            return entry;
        }
    }

    /// <summary>The character under a point of the text view, or null when the point is not over a character.</summary>
    public int? CharacterOffset(Point point)
    {
        var view = TextArea.TextView;
        if (view.Document is not { } document || view.GetPosition(point + view.ScrollOffset) is not { } position)
        {
            return null;
        }

        var offset = document.GetOffset(position.Location);
        if (offset >= document.TextLength)
        {
            return null;
        }

        var segment = new AvaloniaEdit.Document.TextSegment { StartOffset = offset, Length = 1 };
        var hit = AvaloniaEdit.Rendering.BackgroundGeometryBuilder.GetRectsForSegment(view, segment, false)
            .Any(rect => rect.Inflate(2).Contains(point));
        return hit ? offset : null;
    }
}
