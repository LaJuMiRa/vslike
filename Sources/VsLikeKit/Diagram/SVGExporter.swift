import CoreGraphics
import Foundation

/// Exports a diagram as a standalone SVG (light theme, same geometry as the canvas).
public enum SVGExporter {
    public static func export(model: DiagramModel, document: DiagramDocument) -> String {
        let boxes = DiagramGeometry.boxes(model: model, document: document)
        let bounds = DiagramGeometry.bounds(boxes)
        let ink = "#2b3140"
        var svg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="\(f(bounds.minX)) \(f(bounds.minY)) \(f(bounds.width)) \(f(bounds.height))" \
        width="\(f(bounds.width))" height="\(f(bounds.height))" font-family="SF Mono, Menlo, monospace" font-size="\(f(BoxMetrics.fontSize))">
          <defs>
            <marker id="triangle" viewBox="0 0 12 12" refX="12" refY="6" markerWidth="12" markerHeight="12" orient="auto"><path d="M0,0 L12,6 L0,12 z" fill="#ffffff" stroke="\(ink)"/></marker>
            <marker id="arrow" viewBox="0 0 12 12" refX="12" refY="6" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L12,6 L0,12" fill="none" stroke="\(ink)"/></marker>
            <marker id="diamond" viewBox="0 0 16 10" refX="0" refY="5" markerWidth="16" markerHeight="10" orient="auto"><path d="M0,5 L8,0 L16,5 L8,10 z" fill="\(ink)"/></marker>
          </defs>
          <rect x="\(f(bounds.minX))" y="\(f(bounds.minY))" width="\(f(bounds.width))" height="\(f(bounds.height))" fill="#f7f8fa"/>

        """
        for edge in DiagramGeometry.edges(model: model, boxes: boxes) {
            let (dash, end, start): (String, String, String?) = switch edge.relation.kind {
            case .inheritance: ("", "url(#triangle)", nil)
            case .implementation: (" stroke-dasharray=\"6 4\"", "url(#triangle)", nil)
            case .composition: ("", "url(#arrow)", "url(#diamond)")
            case .association: ("", "url(#arrow)", nil)
            }
            let markers = " marker-end=\"\(end)\"" + (start.map { " marker-start=\"\($0)\"" } ?? "")
            svg += "  <line x1=\"\(f(edge.start.x))\" y1=\"\(f(edge.start.y))\" x2=\"\(f(edge.end.x))\" y2=\"\(f(edge.end.y))\" stroke=\"\(ink)\"\(dash)\(markers)/>\n"
            if let label = edge.relation.label {
                let text = edge.relation.isMany ? label + " [*]" : label
                svg += "  <text x=\"\(f((edge.start.x + edge.end.x) / 2))\" y=\"\(f((edge.start.y + edge.end.y) / 2 - 4))\" fill=\"#6b7280\" font-size=\"10\">\(escape(text))</text>\n"
            }
        }
        for box in boxes {
            let frame = box.frame
            let fill = box.type.isExternal ? "#eceef2" : "#ffffff"
            let stroke = box.type.isExternal ? "#9ca3af" : ink
            svg += "  <g>\n    <rect x=\"\(f(frame.minX))\" y=\"\(f(frame.minY))\" width=\"\(f(frame.width))\" height=\"\(f(frame.height))\" rx=\"8\" fill=\"\(fill)\" stroke=\"\(stroke)\"/>\n"
            if !box.type.stereotype.isEmpty {
                svg += "    <text x=\"\(f(frame.midX))\" y=\"\(f(frame.minY + 15))\" text-anchor=\"middle\" fill=\"#6b7280\" font-size=\"10\">\(escape(box.type.stereotype))</text>\n"
            }
            let italic = box.type.modifiers.contains(.abstract) || box.type.kind == .interface ? " font-style=\"italic\"" : ""
            svg += "    <text x=\"\(f(frame.midX))\" y=\"\(f(frame.minY + 33))\" text-anchor=\"middle\" font-weight=\"bold\" fill=\"\(stroke)\"\(italic)>\(escape(box.type.displayName))</text>\n"
            let lines = BoxMetrics.lines(box.type, collapsed: box.collapsed)
            if !lines.isEmpty {
                let lineY = frame.minY + BoxMetrics.headerHeight
                svg += "    <line x1=\"\(f(frame.minX))\" y1=\"\(f(lineY))\" x2=\"\(f(frame.maxX))\" y2=\"\(f(lineY))\" stroke=\"\(stroke)\"/>\n"
                for (index, line) in lines.enumerated() {
                    let y = lineY + Double(index + 1) * BoxMetrics.lineHeight - 4
                    svg += "    <text x=\"\(f(frame.minX + BoxMetrics.padding))\" y=\"\(f(y))\" fill=\"#1f2329\">\(escape(line))</text>\n"
                }
            }
            svg += "  </g>\n"
        }
        return svg + "</svg>\n"
    }

    static func f(_ value: Double) -> String {
        let rounded = (value * 100).rounded() / 100
        return rounded == rounded.rounded() ? String(Int(rounded)) : String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), rounded)
            .replacingOccurrences(of: #"0+$"#, with: "", options: .regularExpression)
    }

    static func escape(_ text: String) -> String {
        text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
    }
}
