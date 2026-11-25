using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>Tab stops of an inserted snippet; linked placeholders (same index) are edited together.</summary>
public sealed class SnippetSession
{
    private IReadOnlyList<ExpandedSnippet.Placeholder> _placeholders;
    private int _finalCaret;
    private int _current;

    public SnippetSession(ExpandedSnippet snippet, int @base)
    {
        Stops = snippet.Stops;
        _placeholders = [.. snippet.Placeholders.Select(p => p with { Start = p.Start + @base })];
        _finalCaret = snippet.FinalCaret + @base;
    }

    public IReadOnlyList<int> Stops { get; }

    public TextRange? FirstSelection
    {
        get
        {
            if (Stops.Count == 0)
            {
                return null;
            }

            return Find(Stops[0]) is { } placeholder ? new TextRange(placeholder.Start, placeholder.Length) : null;
        }
    }

    public bool Next(CodeTextView view)
    {
        _current++;
        if (_current >= Stops.Count)
        {
            view.SetSelectedRange(new TextRange(_finalCaret, 0));
            return true;
        }

        Select(view);
        return true;
    }

    public bool Previous(CodeTextView view)
    {
        if (_current <= 0)
        {
            return false;
        }

        _current--;
        Select(view);
        return true;
    }

    /// <summary>Keeps placeholder positions up to date while the user types into one.</summary>
    public void TextWillChange(TextRange range, string replacement)
    {
        var delta = replacement.Length - range.Length;
        if (_current >= Stops.Count)
        {
            return;
        }

        var index = Stops[_current];
        ExpandedSnippet.Placeholder? active = _placeholders
            .Where(p => p.Index == index && range.Location >= p.Start && range.End <= p.Start + p.Length)
            .Select(p => (ExpandedSnippet.Placeholder?)p)
            .FirstOrDefault();
        _placeholders =
        [
            .. _placeholders.Select(placeholder =>
            {
                if (active is { } current && placeholder == current)
                {
                    return placeholder with { Length = placeholder.Length + delta };
                }

                return placeholder.Start >= range.End ? placeholder with { Start = placeholder.Start + delta } : placeholder;
            })
        ];
        if (_finalCaret >= range.End)
        {
            _finalCaret += delta;
        }
    }

    public void SelectionChanged(TextRange range) => _ = range;

    private void Select(CodeTextView view)
    {
        if (Find(Stops[_current]) is { } placeholder)
        {
            view.SetSelectedRange(new TextRange(placeholder.Start, placeholder.Length));
        }
    }

    private ExpandedSnippet.Placeholder? Find(int index) =>
        _placeholders.Where(p => p.Index == index).Select(p => (ExpandedSnippet.Placeholder?)p).FirstOrDefault();
}
