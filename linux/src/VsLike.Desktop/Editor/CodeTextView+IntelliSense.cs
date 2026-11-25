using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>Completion, signature help and hover of the code editor.</summary>
public sealed partial class CodeTextView
{
    // MARK: Completion
    public string TypedPrefix
    {
        get
        {
            var caret = SelectedRange.Location;
            return caret >= CompletionStart ? Text[CompletionStart..caret] : string.Empty;
        }
    }

    public void TriggerCompletion() => RequestCompletion();

    public void MaybeTriggerCompletion(char typed)
    {
        if (Workspace.Engine is not { } engine || !Workspace.IsEngineReady || Document is not { } document)
        {
            return;
        }

        var offset = SelectedRange.Location;
        var token = EditorTasks.Renew(ref _completionTask);
        _ = EditorTasks.Run(async () =>
        {
            await Task.Delay(typed == '.' ? 0 : 40, token);
            Workspace.SyncNow(document);

            // Only the first letter of a word opens the list; later letters filter it.
            if (char.IsLetter(typed) || typed == '_')
            {
                var word = WordRange(Math.Max(0, offset - 1));
                if (word.Length != 1)
                {
                    return;
                }
            }

            if (!await engine.ShouldTriggerCompletion(document.Path, offset, typed, token))
            {
                return;
            }

            await LoadCompletion(offset, token);
        });
    }

    public void RequestCompletion()
    {
        var token = EditorTasks.Renew(ref _completionTask);
        _ = EditorTasks.Run(() => LoadCompletion(SelectedRange.Location, token));
    }

    public async Task LoadCompletion(int offset, CancellationToken cancellation)
    {
        if (Workspace.Engine is not { } engine || !Workspace.IsEngineReady || Document is not { } document)
        {
            return;
        }

        Workspace.SyncNow(document);
        var list = await engine.Completion(document.Path, offset, cancellation);
        if (list is null || cancellation.IsCancellationRequested || SelectedRange.Location < list.SpanStart)
        {
            return;
        }

        var prefixStart = list.SpanStart;
        var prefix = Text.Substring(prefixStart, Math.Max(0, SelectedRange.Location - prefixStart));
        var snippets = VsLike.Kit.Snippet.CSharp
            .Where(s => prefix.Length > 0 && s.Shortcut.StartsWith(prefix.ToLowerInvariant(), StringComparison.Ordinal))
            .Select((snippet, index) => new CompletionItem(snippet.Shortcut, CompletionKind.Snippet, -(index + 1), snippet.Shortcut, snippet.Shortcut, snippet.Title));
        CompletionList = new CompletionList([.. snippets, .. list.Items], list.SpanStart, list.SpanLength);
        CompletionStart = list.SpanStart;
        UpdateCompletionFilter(show: true);
    }

    public void UpdateCompletionFilter(bool show = false)
    {
        if (CompletionList is not { } list || !(show || Completion.IsVisible))
        {
            return;
        }

        var caret = SelectedRange.Location;
        if (caret < CompletionStart)
        {
            DismissCompletion();
            return;
        }

        var prefix = TypedPrefix;
        List<CompletionItem> filtered;
        if (prefix.Length == 0)
        {
            filtered = [.. list.Items.OrderBy(i => i.SortText, StringComparer.CurrentCultureIgnoreCase)];
        }
        else
        {
            filtered =
            [
                .. list.Items
                    .Select(item => (Item: item, Score: FuzzyMatcher.Score(prefix, item.FilterText)))
                    .Where(p => p.Score is not null)
                    .Select(p => (p.Item, Score: p.Score!.Value
                        + (p.Item.FilterText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 100 : 0)
                        + (p.Item.FilterText.StartsWith(prefix, StringComparison.Ordinal) ? 20 : 0)))
                    .OrderByDescending(p => p.Score)
                    .ThenBy(p => p.Item.SortText, StringComparer.Ordinal)
                    .Select(p => p.Item)
            ];
        }

        if (filtered.Count == 0 || (filtered.Count == 1 && filtered[0].FilterText == prefix && filtered[0].Kind != CompletionKind.Snippet))
        {
            DismissCompletion();
            return;
        }

        if (TopLevel.GetTopLevel(this) is null || CaretRect(CompletionStart) is not { } anchor)
        {
            return;
        }

        Completion.Show([.. filtered.Take(400)], TextArea.TextView, anchor);
    }

    public void DismissCompletion()
    {
        Completion.Dismiss();
        CompletionList = null;
        _detailTask?.Cancel();
    }

    public void LoadCompletionDetail(CompletionItem item)
    {
        var token = EditorTasks.Renew(ref _detailTask);
        if (item.Handle < 0 || Workspace.Engine is not { } engine || Document is not { } document)
        {
            Completion.SetDetail(item.Kind == CompletionKind.Snippet ? VsLike.Kit.Snippet.Find(item.DisplayText)?.Body.Replace("\t", "    ", StringComparison.Ordinal) : null);
            return;
        }

        _ = EditorTasks.Run(async () =>
        {
            await Task.Delay(120, token);
            var text = await engine.CompletionDescription(document.Path, item.Handle, token);
            if (!token.IsCancellationRequested && Completion.IsVisible)
            {
                Completion.SetDetail(text);
            }
        });
    }

    public void CommitCompletion(CompletionItem item, char? character = null)
    {
        var start = CompletionStart;
        var end = SelectedRange.Location;
        DismissCompletion();
        if (item.Kind == CompletionKind.Snippet && VsLike.Kit.Snippet.Find(item.DisplayText) is { } definition)
        {
            Expand(definition, new TextRange(start, end - start));
            return;
        }

        if (Workspace.Engine is not { } engine || Document is not { } document)
        {
            return;
        }

        Workspace.SyncNow(document);
        _ = EditorTasks.Run(async () =>
        {
            var edit = await engine.ResolveCompletion(document.Path, item.Handle);
            var file = edit.Files.FirstOrDefault(f => RealPath.Resolve(f.FilePath) == document.Path);
            if (file is { Edits.Count: > 0 })
            {
                ApplyCompletionEdits(edit, file, start, end, document);
            }
            else
            {
                InsertText(item.DisplayText, new TextRange(start, end - start));
            }

            if (character is { } typed)
            {
                InsertText(typed.ToString(), SelectedRange);
            }
            else if (item.Kind is CompletionKind.Method or CompletionKind.ExtensionMethod)
            {
                RefreshSignatureHelp();
            }
        });
    }

    // MARK: Signature help
    public void ShowSignatureHelp() => RefreshSignatureHelp();

    public void RefreshSignatureHelp()
    {
        if (Workspace.Engine is not { } engine || !Workspace.IsEngineReady || Document is not { } document)
        {
            return;
        }

        var token = EditorTasks.Renew(ref _signatureTask);
        var offset = SelectedRange.Location;
        _ = EditorTasks.Run(async () =>
        {
            await Task.Delay(30, token);
            Workspace.SyncNow(document);
            var help = await engine.SignatureHelp(document.Path, offset, token);

            // A newer refresh or a dismissal took over meanwhile, or the caret has moved on.
            if (token.IsCancellationRequested || SelectedRange.Location != offset)
            {
                return;
            }

            if (help is not { Signatures.Count: > 0 } || !TextArea.IsKeyboardFocusWithin || CaretRect(offset) is not { } anchor)
            {
                SignaturePopup.Dismiss();
                return;
            }

            SignaturePopup.Show(SignatureHelpCard.Create(help), TextArea.TextView, anchor, above: true);
        });
    }

    public void DismissSignatureHelp()
    {
        _signatureTask?.Cancel();
        SignaturePopup.Dismiss();
    }

    public void PositionPopups()
    {
        if (Completion.IsVisible)
        {
            UpdateCompletionFilter();
        }

        if (SignaturePopup.IsVisible)
        {
            RefreshSignatureHelp();
        }
    }

    // MARK: Hover
    public async Task ShowHover(int offset, Point point, CancellationToken cancellation)
    {
        if (Document is not { } document || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        var anchor = new Rect(point.X, point.Y + 8, 1, 1);
        if (Workspace.Debugger.IsStopped)
        {
            var word = Expression(offset);
            if (await Workspace.Debugger.HoverValue(word) is { } value && !cancellation.IsCancellationRequested)
            {
                var text = new SelectableTextBlock { Text = value, FontFamily = new FontFamily(Preferences.EditorFontFamily), FontSize = 12, Foreground = Palette.Label.Brush };
                HoverPopup.Show(text, TextArea.TextView, anchor, above: false);
                return;
            }
        }

        var local = Diagnostics.Where(d => d.Start <= offset && offset <= d.Start + Math.Max(d.Length, 1) && d.Severity != DiagnosticSeverity.Hidden).ToList();
        var signature = string.Empty;
        string? documentation = null;
        if (Workspace.Engine is { } engine && Workspace.IsEngineReady && document.IsCSharp)
        {
            Workspace.SyncNow(document);
            if (await engine.QuickInfo(document.Path, offset, cancellation) is { } info)
            {
                signature = info.Signature;
                documentation = info.Documentation;
            }
        }

        if (cancellation.IsCancellationRequested || (signature.Length == 0 && local.Count == 0))
        {
            return;
        }

        HoverPopup.Show(QuickInfoCard.Create(signature, documentation, local), TextArea.TextView, anchor, above: false);
    }

    /// <summary>The identifier chain under the mouse (<c>car.Name</c>) for debugger hover.</summary>
    public string Expression(int offset)
    {
        var text = Text;
        var start = Math.Clamp(offset, 0, text.Length);
        var end = start;
        while (start > 0 && (CSharpLexer.IsIdentifierPart(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        while (end < text.Length && CSharpLexer.IsIdentifierPart(text[end]))
        {
            end++;
        }

        return text[start..end];
    }

    /// <summary>The line rect at a character in text view coordinates (NSTextView's <c>firstRect(forCharacterRange:)</c>).</summary>
    public Rect? CaretRect(int offset)
    {
        var view = TextArea.TextView;
        if (view.Document is not { } document)
        {
            return null;
        }

        view.EnsureVisualLines();
        var location = document.GetLocation(Math.Clamp(offset, 0, document.TextLength));
        var position = new TextViewPosition(location);
        var top = view.GetVisualPosition(position, VisualYPosition.LineTop) - view.ScrollOffset;
        var bottom = view.GetVisualPosition(position, VisualYPosition.LineBottom) - view.ScrollOffset;
        return double.IsFinite(top.X) && double.IsFinite(top.Y) ? new Rect(top.X, top.Y, 1, Math.Max(1, bottom.Y - top.Y)) : null;
    }

    private void ApplyCompletionEdits(WorkspaceEdit edit, FileEdit file, int start, int end, CodeDocument document)
    {
        var storage = Editor.Document;
        var caretShift = 0;
        var caret = end;
        storage.BeginUpdate();
        try
        {
            foreach (var change in TextEditing.CompletionEdits(file.Edits, start, end).OrderByDescending(c => c.Start))
            {
                var location = Math.Clamp(change.Start, 0, storage.TextLength);
                storage.Replace(location, Math.Clamp(change.Length, 0, storage.TextLength - location), change.NewText);
                var delta = change.NewText.Length - change.Length;
                if (change.Start + change.Length <= end - (end - start))
                {
                    caretShift += delta;
                }

                if (change.Start <= start && change.Start + change.Length >= start)
                {
                    caret = change.Start + change.NewText.Length;
                }
            }
        }
        finally
        {
            storage.EndUpdate();
        }

        if (edit.CaretOffset is { } target && edit.CaretFile is { } caretFile && RealPath.Resolve(caretFile) == document.Path)
        {
            SetSelectedRange(new TextRange(target, 0));
        }
        else
        {
            SetSelectedRange(new TextRange(caret + caretShift, 0));
        }
    }
}
