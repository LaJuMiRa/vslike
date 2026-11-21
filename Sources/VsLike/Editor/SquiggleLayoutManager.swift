import AppKit

/// Draws diagnostic underlines as wavy lines under exactly the affected characters. `CodeTextView.setDiagnostics`
/// sets the underline as temporary attributes; the colour comes from `.underlineColor`, and a thick underline style
/// marks errors.
final class SquiggleLayoutManager: NSLayoutManager {
    private static let amplitude: CGFloat = 1.6
    private static let wavelength: CGFloat = 4.5

    override func drawUnderline(
        forGlyphRange glyphRange: NSRange,
        underlineType: NSUnderlineStyle,
        baselineOffset: CGFloat,
        lineFragmentRect lineRect: NSRect,
        lineFragmentGlyphRange: NSRange,
        containerOrigin: NSPoint
    ) {
        guard let container = textContainer(forGlyphAt: glyphRange.location, effectiveRange: nil) else { return }
        let characterIndex = characterIndexForGlyph(at: glyphRange.location)
        let color = temporaryAttribute(.underlineColor, atCharacterIndex: characterIndex, effectiveRange: nil) as? NSColor ?? .systemRed
        let bounds = boundingRect(forGlyphRange: glyphRange, in: container)
        let baseline = location(forGlyphAt: glyphRange.location).y
        let y = containerOrigin.y + lineRect.minY + baseline + Self.amplitude + 1.5
        let minX = containerOrigin.x + bounds.minX
        let maxX = max(containerOrigin.x + bounds.maxX, minX + Self.wavelength)

        let path = NSBezierPath()
        path.lineWidth = underlineType.contains(.thick) ? 1.5 : 1.2
        path.lineCapStyle = .round
        path.lineJoinStyle = .round
        path.move(to: NSPoint(x: minX, y: y))
        var x = minX
        var up = true
        let half = Self.wavelength / 2
        while x < maxX {
            let next = min(x + half, maxX)
            path.curve(
                to: NSPoint(x: next, y: y),
                controlPoint1: NSPoint(x: x + half / 2, y: y + (up ? -Self.amplitude : Self.amplitude) * 1.3),
                controlPoint2: NSPoint(x: next - half / 2, y: y + (up ? -Self.amplitude : Self.amplitude) * 1.3)
            )
            x = next
            up.toggle()
        }
        color.setStroke()
        path.stroke()
    }
}
