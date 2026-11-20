using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace VsLike.Editor.Snippets;

/// <summary>
/// Ein eingefügtes Snippet: Tab springt zum nächsten Platzhalter, verknüpfte Platzhalter (gleiche Nummer)
/// werden beim Verlassen angeglichen, Esc/Enter beenden die Sitzung.
/// </summary>
public sealed class SnippetSession
{
    private readonly TextEditor _editor;
    private readonly List<(int Index, TextAnchor Start, TextAnchor End)> _placeholders;
    private readonly TextAnchor _final;
    private readonly List<int> _order;
    private int _current = -1;

    private SnippetSession(TextEditor editor, List<(int Index, TextAnchor Start, TextAnchor End)> placeholders, TextAnchor final)
    {
        _editor = editor;
        _placeholders = placeholders;
        _final = final;
        _order = [.. placeholders.Select(p => p.Index).Distinct().Order()];
    }

    public bool IsActive { get; private set; } = true;

    /// <summary>Replaces <paramref name="replaceLength"/> characters at <paramref name="offset"/> with the snippet and selects the first placeholder.</summary>
    public static SnippetSession Insert(TextEditor editor, int offset, int replaceLength, ExpandedSnippet snippet)
    {
        var document = editor.Document;
        document.Replace(offset, replaceLength, snippet.Text);
        var placeholders = snippet.Placeholders.Select(p =>
        {
            var start = document.CreateAnchor(offset + p.Start);
            start.MovementType = AnchorMovementType.BeforeInsertion;
            var end = document.CreateAnchor(offset + p.Start + p.Length);
            end.MovementType = AnchorMovementType.AfterInsertion;
            return (p.Index, start, end);
        }).ToList();
        var final = document.CreateAnchor(offset + snippet.FinalCaret);
        var session = new SnippetSession(editor, placeholders, final);
        session.Next();
        return session;
    }

    /// <summary>Moves to the next placeholder, or to the final caret position which ends the session.</summary>
    public void Next()
    {
        SyncLinked();
        _current++;
        if (_current >= _order.Count)
        {
            End();
            return;
        }

        var (_, start, end) = _placeholders.First(p => p.Index == _order[_current]);
        _editor.Select(start.Offset, end.Offset - start.Offset);
        _editor.CaretOffset = end.Offset;
    }

    public void End()
    {
        if (!IsActive)
        {
            return;
        }

        SyncLinked();
        IsActive = false;
        _editor.TextArea.ClearSelection();
        _editor.CaretOffset = _final.Offset;
    }

    private void SyncLinked()
    {
        if (_current < 0 || _current >= _order.Count)
        {
            return;
        }

        var index = _order[_current];
        var linked = _placeholders.Where(p => p.Index == index).ToList();
        var document = _editor.Document;
        var text = document.GetText(linked[0].Start.Offset, linked[0].End.Offset - linked[0].Start.Offset);
        document.BeginUpdate();
        try
        {
            foreach (var (_, start, end) in linked.Skip(1).OrderByDescending(p => p.Start.Offset))
            {
                document.Replace(start.Offset, end.Offset - start.Offset, text);
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }
}
