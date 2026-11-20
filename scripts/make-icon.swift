// Draws the VsLike app icon (own motif: amber code brackets on a slate tile) into an .iconset folder.
// Usage: make-icon <output.iconset>
import AppKit

let output = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset"
try? FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)

func draw(size: CGFloat) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let context = NSGraphicsContext.current!.cgContext
    let space = CGColorSpaceCreateDeviceRGB()

    // macOS icon grid: the tile covers ~80 % of the canvas with a continuous-corner rounded rectangle.
    let inset = size * 0.1
    let tile = CGRect(x: inset, y: inset * 1.08, width: size - 2 * inset, height: size - 2 * inset)
    let radius = tile.width * 0.225
    let path = CGPath(roundedRect: tile, cornerWidth: radius, cornerHeight: radius, transform: nil)

    context.saveGState()
    context.setShadow(offset: CGSize(width: 0, height: -size * 0.012), blur: size * 0.03, color: CGColor(gray: 0, alpha: 0.35))
    context.addPath(path)
    context.setFillColor(CGColor(red: 0.12, green: 0.13, blue: 0.16, alpha: 1))
    context.fillPath()
    context.restoreGState()

    context.saveGState()
    context.addPath(path)
    context.clip()
    let background = CGGradient(colorsSpace: space, colors: [
        CGColor(red: 0.25, green: 0.28, blue: 0.35, alpha: 1),
        CGColor(red: 0.11, green: 0.12, blue: 0.16, alpha: 1),
    ] as CFArray, locations: [0, 1])!
    context.drawLinearGradient(background, start: CGPoint(x: 0, y: tile.maxY), end: CGPoint(x: 0, y: tile.minY), options: [])
    // Soft light from the top.
    let glow = CGGradient(colorsSpace: space, colors: [CGColor(gray: 1, alpha: 0.16), CGColor(gray: 1, alpha: 0)] as CFArray, locations: [0, 1])!
    context.drawRadialGradient(glow, startCenter: CGPoint(x: tile.midX, y: tile.maxY), startRadius: 0, endCenter: CGPoint(x: tile.midX, y: tile.maxY), endRadius: tile.width * 0.8, options: [])
    context.restoreGState()

    // Hairline edge.
    context.addPath(path)
    context.setStrokeColor(CGColor(gray: 1, alpha: 0.12))
    context.setLineWidth(max(1, size * 0.004))
    context.strokePath()

    // "</>" in amber with a slight gradient.
    let mid = CGPoint(x: tile.midX, y: tile.midY + size * 0.01)
    let height = size * 0.19
    let width = size * 0.12
    let gap = size * 0.115
    let glyph = CGMutablePath()
    glyph.move(to: CGPoint(x: mid.x - gap, y: mid.y + height))
    glyph.addLine(to: CGPoint(x: mid.x - gap - width, y: mid.y))
    glyph.addLine(to: CGPoint(x: mid.x - gap, y: mid.y - height))
    glyph.move(to: CGPoint(x: mid.x + gap, y: mid.y + height))
    glyph.addLine(to: CGPoint(x: mid.x + gap + width, y: mid.y))
    glyph.addLine(to: CGPoint(x: mid.x + gap, y: mid.y - height))
    glyph.move(to: CGPoint(x: mid.x + size * 0.045, y: mid.y + height * 1.05))
    glyph.addLine(to: CGPoint(x: mid.x - size * 0.045, y: mid.y - height * 1.05))
    let stroked = glyph.copy(strokingWithWidth: size * 0.062, lineCap: .round, lineJoin: .round, miterLimit: 10)
    context.saveGState()
    context.setShadow(offset: .zero, blur: size * 0.05, color: CGColor(red: 1, green: 0.7, blue: 0.25, alpha: 0.45))
    context.addPath(stroked)
    context.setFillColor(CGColor(red: 0.96, green: 0.70, blue: 0.28, alpha: 1))
    context.fillPath()
    context.restoreGState()
    context.saveGState()
    context.addPath(stroked)
    context.clip()
    let amber = CGGradient(colorsSpace: space, colors: [
        CGColor(red: 1.0, green: 0.84, blue: 0.48, alpha: 1),
        CGColor(red: 0.93, green: 0.58, blue: 0.16, alpha: 1),
    ] as CFArray, locations: [0, 1])!
    context.drawLinearGradient(amber, start: CGPoint(x: 0, y: mid.y + height), end: CGPoint(x: 0, y: mid.y - height), options: [])
    context.restoreGState()

    NSGraphicsContext.restoreGraphicsState()
    return rep
}

for base in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = CGFloat(base * scale)
        let name = scale == 1 ? "icon_\(base)x\(base).png" : "icon_\(base)x\(base)@2x.png"
        let data = draw(size: pixels).representation(using: .png, properties: [:])!
        try! data.write(to: URL(fileURLWithPath: output).appendingPathComponent(name))
    }
}
