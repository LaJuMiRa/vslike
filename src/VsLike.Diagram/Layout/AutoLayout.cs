using VsLike.Diagram.Model;

namespace VsLike.Diagram.Layout;

/// <summary>
/// Einfaches Schichtenlayout: Basistypen und Interfaces stehen über ihren Ableitungen, unabhängige Typen in weiteren
/// Spalten. Vorhandene Positionen bleiben, nur Typen ohne Position werden platziert (außer bei <c>all</c>).
/// </summary>
public static class AutoLayout
{
    private const double HorizontalGap = 60;
    private const double VerticalGap = 80;

    public static DiagramDocument Arrange(DiagramDocument document, DiagramModel model, bool all)
    {
        var shown = document.Types.Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
        var types = model.Types.Where(t => shown.Contains(t.FullName)).ToList();
        var toPlace = all ? types : types.Where(t => document.Find(t.FullName) is not { } p || double.IsNaN(p.X)).ToList();
        if (toPlace.Count == 0)
        {
            return document;
        }

        var parents = model.Relations.Where(r => r.Kind is RelationKind.Inheritance or RelationKind.Implementation && shown.Contains(r.To) && shown.Contains(r.From)).ToLookup(r => r.From, r => r.To);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string name, int guard)
        {
            if (depth.TryGetValue(name, out var known))
            {
                return known;
            }

            var value = guard > 32 || !parents[name].Any() ? 0 : parents[name].Max(p => Depth(p, guard + 1)) + 1;
            depth[name] = value;
            return value;
        }

        var placed = all ? [] : document.Types.Where(t => !double.IsNaN(t.X) && !toPlace.Any(p => p.FullName == t.FullName)).ToList();
        var startY = placed.Count == 0 ? BoxMetrics.GridSize : placed.Max(p => p.Y + BoxMetrics.Size(model.Find(p.FullName) ?? Placeholder(p.FullName), p.Collapsed).Height) + VerticalGap;
        var result = document;
        var y = startY;
        foreach (var layer in toPlace.GroupBy(t => Depth(t.FullName, 0)).OrderBy(g => g.Key))
        {
            var x = BoxMetrics.GridSize;
            var rowHeight = 0.0;
            foreach (var type in layer.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var collapsed = document.Find(type.FullName)?.Collapsed ?? false;
                var (width, height) = BoxMetrics.Size(type, collapsed);
                result = result.WithPlacement(new TypePlacement(type.FullName, BoxMetrics.Snap(x), BoxMetrics.Snap(y), collapsed));
                x += width + HorizontalGap;
                rowHeight = Math.Max(rowHeight, height);
            }

            y += rowHeight + VerticalGap;
        }

        return result;
    }

    private static DiagramType Placeholder(string fullName) => new(fullName, fullName, string.Empty, DiagramTypeKind.Class);
}
