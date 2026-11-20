using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VsLike.Core.Code;
using VsLike.Editor.Highlighting;

namespace VsLike.Editor.Intelligence;

/// <summary>
/// Färbt Bezeichner nach ihrer Bedeutung (Typ, Methode, Feld …) über das TextMate-Highlighting hinweg.
/// Spans gelten nur für die Textversion, für die sie berechnet wurden.
/// </summary>
public sealed class SemanticColorizer : DocumentColorizingTransformer
{
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        [SemanticClassifications.Class] = "#4FC1B1",
        [SemanticClassifications.Record] = "#4FC1B1",
        [SemanticClassifications.Struct] = "#7AD3A8",
        [SemanticClassifications.Interface] = "#9FD8C9",
        [SemanticClassifications.Enum] = "#7AD3A8",
        [SemanticClassifications.Delegate] = "#4FC1B1",
        [SemanticClassifications.TypeParameter] = "#9FD8C9",
        [SemanticClassifications.Method] = "#8EB9F2",
        [SemanticClassifications.ExtensionMethod] = "#8EB9F2",
        [SemanticClassifications.Property] = "#E7C9A0",
        [SemanticClassifications.Event] = "#E7C9A0",
        [SemanticClassifications.Field] = "#D4B3E8",
        [SemanticClassifications.Constant] = "#C9A0E6",
        [SemanticClassifications.EnumMember] = "#C9A0E6",
        [SemanticClassifications.Parameter] = "#B9C4D6",
        [SemanticClassifications.Namespace] = "#AAB2C0",
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        [SemanticClassifications.Class] = "#137A70",
        [SemanticClassifications.Record] = "#137A70",
        [SemanticClassifications.Struct] = "#2E7D4F",
        [SemanticClassifications.Interface] = "#3A8C7C",
        [SemanticClassifications.Enum] = "#2E7D4F",
        [SemanticClassifications.Delegate] = "#137A70",
        [SemanticClassifications.TypeParameter] = "#3A8C7C",
        [SemanticClassifications.Method] = "#2B5FA8",
        [SemanticClassifications.ExtensionMethod] = "#2B5FA8",
        [SemanticClassifications.Property] = "#8A5A12",
        [SemanticClassifications.Event] = "#8A5A12",
        [SemanticClassifications.Field] = "#7A3FA0",
        [SemanticClassifications.Constant] = "#8A3FB5",
        [SemanticClassifications.EnumMember] = "#8A3FB5",
        [SemanticClassifications.Parameter] = "#3F4A5C",
        [SemanticClassifications.Namespace] = "#5B6270",
    };

    private IReadOnlyList<ClassifiedSpan> _spans = [];
    private ITextSourceVersion? _version;
    private Dictionary<string, IBrush> _brushes = CreateBrushes(Dark);

    public int SpanCount => _spans.Count;

    public void SetTheme(EditorThemeKind kind) => _brushes = CreateBrushes(kind == EditorThemeKind.Dark ? Dark : Light);

    /// <summary>Stores spans computed for <paramref name="version"/>; returns false if the text changed meanwhile.</summary>
    public bool SetSpans(IReadOnlyList<ClassifiedSpan> spans, ITextSourceVersion? version, ITextSourceVersion? currentVersion)
    {
        if (version is not null && currentVersion is not null && !version.BelongsToSameDocumentAs(currentVersion))
        {
            return false;
        }

        if (version is not null && currentVersion is not null && version.CompareAge(currentVersion) != 0)
        {
            return false;
        }

        _spans = [.. spans.OrderBy(s => s.Start)];
        _version = version;
        return true;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_spans.Count == 0 || (_version is not null && CurrentContext.Document.Version is { } current && _version.CompareAge(current) != 0))
        {
            return;
        }

        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;
        var index = FirstIndexAtOrAfter(lineStart);
        for (var i = index; i < _spans.Count && _spans[i].Start < lineEnd; i++)
        {
            var span = _spans[i];
            if (!_brushes.TryGetValue(span.Classification, out var brush))
            {
                continue;
            }

            var start = Math.Max(span.Start, lineStart);
            var end = Math.Min(span.Start + span.Length, lineEnd);
            if (end > start)
            {
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }
    }

    private static Dictionary<string, IBrush> CreateBrushes(IReadOnlyDictionary<string, string> colors)
        => colors.ToDictionary(c => c.Key, c => (IBrush)new SolidColorBrush(Color.Parse(c.Value)), StringComparer.Ordinal);

    private int FirstIndexAtOrAfter(int offset)
    {
        int low = 0, high = _spans.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (_spans[mid].Start + _spans[mid].Length <= offset)
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
}
