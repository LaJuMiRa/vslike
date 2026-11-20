using System.Globalization;
using System.Net;
using System.Text;
using VsLike.Diagram.Layout;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Export;

/// <summary>Exportiert das Diagramm als eigenständiges SVG (helle Farben, eingebettete Pfeilspitzen).</summary>
public static class SvgExporter
{
    public static string Export(DiagramModel model, DiagramDocument document)
    {
        var boxes = DiagramGeometry.Boxes(model, document);
        var (x, y, width, height) = DiagramGeometry.Bounds(boxes);
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(x)} {F(y)} {F(width)} {F(height)}\" width=\"{F(width)}\" height=\"{F(height)}\" font-family=\"Menlo, DejaVu Sans Mono, monospace\" font-size=\"{F(BoxMetrics.FontSize)}\">\n");
        svg.Append("""
              <defs>
                <marker id="triangle" viewBox="0 0 12 12" refX="12" refY="6" markerWidth="12" markerHeight="12" orient="auto"><path d="M0,0 L12,6 L0,12 z" fill="#ffffff" stroke="#3a3f4b"/></marker>
                <marker id="arrow" viewBox="0 0 12 12" refX="12" refY="6" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L12,6 L0,12" fill="none" stroke="#3a3f4b"/></marker>
                <marker id="diamond" viewBox="0 0 16 10" refX="0" refY="5" markerWidth="16" markerHeight="10" orient="auto"><path d="M0,5 L8,0 L16,5 L8,10 z" fill="#3a3f4b"/></marker>
              </defs>

            """);
        svg.Append(CultureInfo.InvariantCulture, $"  <rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"#fbfaf7\"/>\n");
        foreach (var (relation, from, to) in DiagramGeometry.Edges(model, boxes))
        {
            var (start, end) = DiagramGeometry.Segment(from, to);
            var (dash, endMarker, startMarker) = relation.Kind switch
            {
                RelationKind.Inheritance => (string.Empty, "url(#triangle)", null),
                RelationKind.Implementation => (" stroke-dasharray=\"6 4\"", "url(#triangle)", null),
                RelationKind.Composition => (string.Empty, "url(#arrow)", "url(#diamond)"),
                _ => (string.Empty, "url(#arrow)", (string?)null),
            };
            var markers = $" marker-end=\"{endMarker}\"" + (startMarker is null ? string.Empty : $" marker-start=\"{startMarker}\"");
            svg.Append(CultureInfo.InvariantCulture, $"  <line x1=\"{F(start.X)}\" y1=\"{F(start.Y)}\" x2=\"{F(end.X)}\" y2=\"{F(end.Y)}\" stroke=\"#3a3f4b\"{dash}{markers}/>\n");
            if (relation.Label is { } label)
            {
                var text = relation.IsMany ? label + " [*]" : label;
                svg.Append(CultureInfo.InvariantCulture, $"  <text x=\"{F((start.X + end.X) / 2)}\" y=\"{F(((start.Y + end.Y) / 2) - 4)}\" fill=\"#6b7280\" font-size=\"10\">{Escape(text)}</text>\n");
            }
        }

        foreach (var box in boxes)
        {
            var fill = box.Type.IsExternal ? "#eceae4" : "#ffffff";
            var stroke = box.Type.IsExternal ? "#9ca3af" : "#3a3f4b";
            svg.Append(CultureInfo.InvariantCulture, $"  <g>\n    <rect x=\"{F(box.X)}\" y=\"{F(box.Y)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" rx=\"6\" fill=\"{fill}\" stroke=\"{stroke}\"/>\n");
            var stereotype = BoxMetrics.Stereotype(box.Type);
            if (stereotype.Length > 0)
            {
                svg.Append(CultureInfo.InvariantCulture, $"    <text x=\"{F(box.CenterX)}\" y=\"{F(box.Y + 14)}\" text-anchor=\"middle\" fill=\"#6b7280\" font-size=\"10\">{Escape(stereotype)}</text>\n");
            }

            var italic = box.Type.Modifiers.HasFlag(TypeModifiers.Abstract) || box.Type.Kind == DiagramTypeKind.Interface ? " font-style=\"italic\"" : string.Empty;
            svg.Append(CultureInfo.InvariantCulture, $"    <text x=\"{F(box.CenterX)}\" y=\"{F(box.Y + 31)}\" text-anchor=\"middle\" font-weight=\"bold\" fill=\"{stroke}\"{italic}>{Escape(box.Type.DisplayName)}</text>\n");
            var lines = BoxMetrics.Lines(box.Type, box.Collapsed);
            if (lines.Count > 0)
            {
                svg.Append(CultureInfo.InvariantCulture, $"    <line x1=\"{F(box.X)}\" y1=\"{F(box.Y + BoxMetrics.HeaderHeight)}\" x2=\"{F(box.X + box.Width)}\" y2=\"{F(box.Y + BoxMetrics.HeaderHeight)}\" stroke=\"{stroke}\"/>\n");
                for (var i = 0; i < lines.Count; i++)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"    <text x=\"{F(box.X + BoxMetrics.Padding)}\" y=\"{F(box.Y + BoxMetrics.HeaderHeight + ((i + 1) * BoxMetrics.LineHeight) - 4)}\" fill=\"#1f2329\">{Escape(lines[i])}</text>\n");
                }
            }

            svg.Append("  </g>\n");
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
