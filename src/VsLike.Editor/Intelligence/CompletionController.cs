using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using VsLike.Core.Code;
using VsLike.Editor.Snippets;
using CompletionList = VsLike.Core.Code.CompletionList;

namespace VsLike.Editor.Intelligence;

/// <summary>
/// Completion-Liste im Editor (AvaloniaEdit-<see cref="CompletionWindow"/>). Tab und Enter übernehmen den
/// markierten Eintrag; die eigentliche Änderung (inkl. using) liefert der Aufrufer über <see cref="CommitHandler"/>.
/// </summary>
public sealed class CompletionController
{
    private readonly TextEditor _editor;
    private CompletionWindow? _window;

    public CompletionController(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>Commits an entry; arguments are the entry and the current segment (typed prefix) start/length.</summary>
    public delegate Task CommitHandler(CompletionEntry entry, int segmentStart, int segmentLength);

    public bool IsOpen => _window is not null;

    /// <summary>Gets the entries currently offered (for tests and diagnostics).</summary>
    public IReadOnlyList<string> VisibleTexts => _window?.CompletionList.CompletionData.Select(d => d.Text).ToList() ?? [];

    public void Show(
        CompletionList list,
        IReadOnlyList<SnippetDefinition> snippets,
        CommitHandler commit,
        Func<SnippetDefinition, int, int, Task> commitSnippet,
        Func<CompletionEntry, Task<string?>> describe)
    {
        Close();
        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = list.SpanStart,
            EndOffset = list.SpanStart + list.SpanLength,
            CloseWhenCaretAtBeginning = true,
            MinWidth = 320,
        };
        window.CompletionList.IsFiltering = true;
        window.CompletionList.CompletionAcceptKeys = [Key.Enter, Key.Tab];
        var data = window.CompletionList.CompletionData;
        foreach (var entry in list.Items.OrderBy(e => e.SortText, StringComparer.OrdinalIgnoreCase))
        {
            data.Add(new EntryItem(entry, commit, describe));
        }

        foreach (var snippet in snippets)
        {
            data.Add(new SnippetItem(snippet, commitSnippet));
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window = window;
        window.Show();
        var typed = _editor.Document.GetText(list.SpanStart, list.SpanLength);
        if (typed.Length > 0)
        {
            window.CompletionList.SelectItem(typed);
        }
        else if (list.Items.FirstOrDefault(i => i.IsPreselected) is { } preselected)
        {
            window.CompletionList.SelectItem(preselected.FilterText);
        }
    }

    /// <summary>Commits the selected entry when a non-identifier character (e.g. '.', '(' or ';') is typed.</summary>
    public void OnCharacterTyping(char typed)
    {
        if (_window is not null && !(char.IsLetterOrDigit(typed) || typed == '_'))
        {
            if (typed == ' ')
            {
                Close();
                return;
            }

            _window.CompletionList.RequestInsertion(EventArgs.Empty);
        }
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    private static Control CreateContent(string text, string? detail, string badge)
        => new DockPanel
        {
            Children =
            {
                new TextBlock { Text = badge, Width = 26, FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = detail ?? string.Empty, Opacity = 0.55, Margin = new Thickness(12, 0, 0, 0), [DockPanel.DockProperty] = Dock.Right, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
            },
        };

    private static string BadgeOf(CompletionKind kind) => kind switch
    {
        CompletionKind.Class => "cls",
        CompletionKind.Struct => "str",
        CompletionKind.Interface => "ifc",
        CompletionKind.Enum => "enm",
        CompletionKind.EnumMember => "val",
        CompletionKind.Delegate => "dlg",
        CompletionKind.Method or CompletionKind.ExtensionMethod => "fn",
        CompletionKind.Property => "prp",
        CompletionKind.Field => "fld",
        CompletionKind.Event => "evt",
        CompletionKind.Local or CompletionKind.Parameter => "var",
        CompletionKind.Namespace => "ns",
        CompletionKind.Keyword => "kw",
        CompletionKind.TypeParameter => "T",
        CompletionKind.Constant => "con",
        CompletionKind.Snippet => "⟨⟩",
        _ => "·",
    };

    private sealed class EntryItem(CompletionEntry entry, CommitHandler commit, Func<CompletionEntry, Task<string?>> describe) : ICompletionData
    {
        private TextBlock? _description;

        public IImage? Image => null;

        public string Text => entry.FilterText;

        public object Content => CreateContent(entry.DisplayText, entry.InlineDescription, BadgeOf(entry.Kind));

        public object Description
        {
            get
            {
                if (_description is null)
                {
                    _description = new TextBlock { Text = entry.DisplayText, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
                    _ = LoadDescriptionAsync(_description);
                }

                return _description;
            }
        }

        public double Priority => entry.IsPreselected ? 1 : 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => _ = commit(entry, completionSegment.Offset, completionSegment.Length);

        private async Task LoadDescriptionAsync(TextBlock target)
        {
            var text = await describe(entry).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(text))
            {
                target.Text = text;
            }
        }
    }

    private sealed class SnippetItem(SnippetDefinition snippet, Func<SnippetDefinition, int, int, Task> commit) : ICompletionData
    {
        public IImage? Image => null;

        public string Text => snippet.Shortcut;

        public object Content => CreateContent(snippet.Shortcut, snippet.Description, BadgeOf(CompletionKind.Snippet));

        public object Description => snippet.Description;

        public double Priority => -1;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => _ = commit(snippet, completionSegment.Offset, completionSegment.Length);
    }
}
