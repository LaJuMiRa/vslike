using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VsLike.Kit;

/// <summary>Exports a diagram as a standalone SVG (light theme, same geometry as the canvas).</summary>
public static partial class SVGExporter
{
    public static string Export(DiagramModel model, DiagramDocument document)
    {
        var boxes = DiagramGeometry.Boxes(model, document);
        var bounds = DiagramGeometry.Bounds(boxes);
        const string ink = "#2b3140";
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(bounds.MinX)} {F(bounds.MinY)} {F(bounds.Width)} {F(bounds.Height)}\" ")
            .Append($"width=\"{F(bounds.Width)}\" height=\"{F(bounds.Height)}\" font-family=\"SF Mono, Menlo, monospace\" font-size=\"{F(BoxMetrics.FontSize)}\">\n")
            .Append("  <defs>\n")
            .Append($"    <marker id=\"triangle\" viewBox=\"0 0 12 12\" refX=\"12\" refY=\"6\" markerWidth=\"12\" markerHeight=\"12\" orient=\"auto\"><path d=\"M0,0 L12,6 L0,12 z\" fill=\"#ffffff\" stroke=\"{ink}\"/></marker>\n")
            .Append($"    <marker id=\"arrow\" viewBox=\"0 0 12 12\" refX=\"12\" refY=\"6\" markerWidth=\"10\" markerHeight=\"10\" orient=\"auto\"><path d=\"M0,0 L12,6 L0,12\" fill=\"none\" stroke=\"{ink}\"/></marker>\n")
            .Append($"    <marker id=\"diamond\" viewBox=\"0 0 16 10\" refX=\"0\" refY=\"5\" markerWidth=\"16\" markerHeight=\"10\" orient=\"auto\"><path d=\"M0,5 L8,0 L16,5 L8,10 z\" fill=\"{ink}\"/></marker>\n")
            .Append("  </defs>\n")
            .Append($"  <rect x=\"{F(bounds.MinX)}\" y=\"{F(bounds.MinY)}\" width=\"{F(bounds.Width)}\" height=\"{F(bounds.Height)}\" fill=\"#f7f8fa\"/>\n\n");
        foreach (var edge in DiagramGeometry.Edges(model, boxes))
        {
            AppendEdge(svg, edge, ink);
        }

        foreach (var box in boxes)
        {
            AppendBox(svg, box, ink);
        }

        return svg.Append("</svg>\n").ToString();
    }

    internal static string F(double value)
    {
        var rounded = Math.Round(value * 100, MidpointRounding.AwayFromZero) / 100;
        if (rounded == Math.Round(rounded))
        {
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }

        return TrailingZeros().Replace(rounded.ToString("0.00", CultureInfo.InvariantCulture), string.Empty);
    }

    internal static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static void AppendEdge(StringBuilder svg, DiagramEdge edge, string ink)
    {
        var (dash, end, start) = edge.Relation.Kind switch
        {
            RelationKind.Inheritance => (string.Empty, "url(#triangle)", (string?)null),
            RelationKind.Implementation => (" stroke-dasharray=\"6 4\"", "url(#triangle)", null),
            RelationKind.Composition => (string.Empty, "url(#arrow)", "url(#diamond)"),
            _ => (string.Empty, "url(#arrow)", null),
        };
        var markers = $" marker-end=\"{end}\"" + (start is null ? string.Empty : $" marker-start=\"{start}\"");
        svg.Append($"  <line x1=\"{F(edge.Start.X)}\" y1=\"{F(edge.Start.Y)}\" x2=\"{F(edge.End.X)}\" y2=\"{F(edge.End.Y)}\" stroke=\"{ink}\"{dash}{markers}/>\n");
        if (edge.Relation.Label is { } label)
        {
            var text = edge.Relation.IsMany ? label + " [*]" : label;
            svg.Append($"  <text x=\"{F((edge.Start.X + edge.End.X) / 2)}\" y=\"{F(((edge.Start.Y + edge.End.Y) / 2) - 4)}\" fill=\"#6b7280\" font-size=\"10\">{Escape(text)}</text>\n");
        }
    }

    private static void AppendBox(StringBuilder svg, DiagramBox box, string ink)
    {
        var frame = box.Frame;
        var fill = box.Type.IsExternal ? "#eceef2" : "#ffffff";
        var stroke = box.Type.IsExternal ? "#9ca3af" : ink;
        svg.Append($"  <g>\n    <rect x=\"{F(frame.MinX)}\" y=\"{F(frame.MinY)}\" width=\"{F(frame.Width)}\" height=\"{F(frame.Height)}\" rx=\"8\" fill=\"{fill}\" stroke=\"{stroke}\"/>\n");
        if (box.Type.Stereotype.Length > 0)
        {
            svg.Append($"    <text x=\"{F(frame.MidX)}\" y=\"{F(frame.MinY + 15)}\" text-anchor=\"middle\" fill=\"#6b7280\" font-size=\"10\">{Escape(box.Type.Stereotype)}</text>\n");
        }

        var italic = box.Type.Modifiers.HasFlag(TypeModifiers.Abstract) || box.Type.Kind == DiagramTypeKind.Interface ? " font-style=\"italic\"" : string.Empty;
        svg.Append($"    <text x=\"{F(frame.MidX)}\" y=\"{F(frame.MinY + 33)}\" text-anchor=\"middle\" font-weight=\"bold\" fill=\"{stroke}\"{italic}>{Escape(box.Type.DisplayName)}</text>\n");
        var lines = BoxMetrics.Lines(box.Type, box.Collapsed);
        if (lines.Count > 0)
        {
            var lineY = frame.MinY + BoxMetrics.HeaderHeight;
            svg.Append($"    <line x1=\"{F(frame.MinX)}\" y1=\"{F(lineY)}\" x2=\"{F(frame.MaxX)}\" y2=\"{F(lineY)}\" stroke=\"{stroke}\"/>\n");
            for (var index = 0; index < lines.Count; index++)
            {
                var y = lineY + ((index + 1) * BoxMetrics.LineHeight) - 4;
                svg.Append($"    <text x=\"{F(frame.MinX + BoxMetrics.Padding)}\" y=\"{F(y)}\" fill=\"#1f2329\">{Escape(lines[index])}</text>\n");
            }
        }

        svg.Append("  </g>\n");
    }

    [GeneratedRegex("0+$")]
    private static partial Regex TrailingZeros();
}
