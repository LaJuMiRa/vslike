using VsLike.Diagram.Model;

namespace VsLike.Diagram.Layout;

public readonly record struct Box(DiagramType Type, double X, double Y, double Width, double Height, bool Collapsed)
{
    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>Box-Rechtecke und Kantenendpunkte in Diagrammkoordinaten (für Canvas, Trefferprüfung und Export).</summary>
public static class DiagramGeometry
{
    public static IReadOnlyList<Box> Boxes(DiagramModel model, DiagramDocument document)
    {
        var boxes = new List<Box>();
        foreach (var placement in document.Types)
        {
            if (model.Find(placement.FullName) is { } type)
            {
                var (width, height) = BoxMetrics.Size(type, placement.Collapsed);
                boxes.Add(new Box(type, placement.X, placement.Y, width, height, placement.Collapsed));
            }
        }

        return boxes;
    }

    /// <summary>Relations whose both ends are on the diagram.</summary>
    public static IEnumerable<(DiagramRelation Relation, Box From, Box To)> Edges(DiagramModel model, IReadOnlyList<Box> boxes)
    {
        var byName = boxes.ToDictionary(b => b.Type.FullName, StringComparer.Ordinal);
        foreach (var relation in model.Relations)
        {
            if (relation.From != relation.To && byName.TryGetValue(relation.From, out var from) && byName.TryGetValue(relation.To, out var to))
            {
                yield return (relation, from, to);
            }
        }
    }

    /// <summary>Point where the line from the box center towards (x, y) leaves the box.</summary>
    public static (double X, double Y) Border(Box box, double towardsX, double towardsY)
    {
        var dx = towardsX - box.CenterX;
        var dy = towardsY - box.CenterY;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return (box.CenterX, box.CenterY);
        }

        var scale = Math.Min(Math.Abs(dx) < 0.001 ? double.MaxValue : box.Width / 2 / Math.Abs(dx), Math.Abs(dy) < 0.001 ? double.MaxValue : box.Height / 2 / Math.Abs(dy));
        return (box.CenterX + (dx * scale), box.CenterY + (dy * scale));
    }

    public static ((double X, double Y) Start, (double X, double Y) End) Segment(Box from, Box to)
        => (Border(from, to.CenterX, to.CenterY), Border(to, from.CenterX, from.CenterY));

    /// <summary>Extent of all boxes (for export and "fit to window").</summary>
    public static (double X, double Y, double Width, double Height) Bounds(IReadOnlyList<Box> boxes, double margin = 20)
    {
        if (boxes.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var left = boxes.Min(b => b.X) - margin;
        var top = boxes.Min(b => b.Y) - margin;
        var right = boxes.Max(b => b.X + b.Width) + margin;
        var bottom = boxes.Max(b => b.Y + b.Height) + margin;
        return (left, top, right - left, bottom - top);
    }
}
