using VsLike.Core.Code;

namespace VsLike.App.Intelligence;

/// <summary>
/// Roslyn berechnet die Commit-Änderung für den Text beim Öffnen der Liste. Wurde danach weitergetippt, ersetzt die
/// Hauptänderung stattdessen das aktuelle Segment; Änderungen davor (z. B. using) bleiben, dahinter werden verschoben.
/// </summary>
public static class CompletionEditMapper
{
    public static (IReadOnlyList<TextEdit> Edits, int Caret) Map(IReadOnlyList<TextEdit> edits, int? caret, int listSpanStart, int listSpanLength, int segmentStart, int segmentLength)
    {
        var listSpanEnd = listSpanStart + listSpanLength;
        var delta = (segmentStart + segmentLength) - listSpanEnd;
        var main = edits.Where(e => e.Start <= listSpanEnd && e.Start + e.Length >= listSpanStart).OrderBy(e => e.Start).ToList();
        var mapped = new List<TextEdit>();
        foreach (var edit in edits)
        {
            if (main.Contains(edit))
            {
                var start = Math.Min(edit.Start, segmentStart);
                var end = Math.Max(edit.Start + edit.Length + delta, segmentStart + segmentLength);
                mapped.Add(new TextEdit(start, end - start, edit.NewText));
            }
            else if (edit.Start > listSpanEnd)
            {
                mapped.Add(edit with { Start = edit.Start + delta });
            }
            else
            {
                mapped.Add(edit);
            }
        }

        // Die Hauptänderung ersetzt auch die nachgetippten Zeichen, der Ergebnistext ist also derselbe, den Roslyn
        // berechnet hat; Roslyns Cursorposition gilt daher unverändert.
        return (mapped, caret ?? (segmentStart + (main.LastOrDefault()?.NewText.Length ?? segmentLength)));
    }
}
