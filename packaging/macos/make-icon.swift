// Zeichnet das VsLike-Icon (eigenes Motiv: Schiefer-Kachel mit bernsteinfarbenen Klammern) als PNG.
// Aufruf: swift make-icon.swift <ausgabe.png> <größe>
import AppKit

let args = CommandLine.arguments
let output = args.count > 1 ? args[1] : "icon.png"
let size = CGFloat(args.count > 2 ? Double(args[2]) ?? 1024 : 1024)
let space = CGColorSpaceCreateDeviceRGB()
guard let ctx = CGContext(data: nil, width: Int(size), height: Int(size), bitsPerComponent: 8, bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { exit(1) }

let inset = size * 0.09
let tile = CGRect(x: inset, y: inset, width: size - 2 * inset, height: size - 2 * inset)
let path = CGPath(roundedRect: tile, cornerWidth: size * 0.19, cornerHeight: size * 0.19, transform: nil)
ctx.addPath(path)
ctx.clip()
let gradient = CGGradient(colorsSpace: space, colors: [CGColor(red: 0.16, green: 0.18, blue: 0.22, alpha: 1), CGColor(red: 0.09, green: 0.10, blue: 0.13, alpha: 1)] as CFArray, locations: [0, 1])!
ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: size), end: CGPoint(x: 0, y: 0), options: [])
ctx.resetClip()

let amber = CGColor(red: 0.89, green: 0.65, blue: 0.23, alpha: 1)
ctx.setStrokeColor(amber)
ctx.setLineWidth(size * 0.07)
ctx.setLineCap(.round)
ctx.setLineJoin(.round)
let mid = size / 2
let h = size * 0.20
let w = size * 0.13
// "<"
ctx.move(to: CGPoint(x: mid - size * 0.10, y: mid + h))
ctx.addLine(to: CGPoint(x: mid - size * 0.10 - w, y: mid))
ctx.addLine(to: CGPoint(x: mid - size * 0.10, y: mid - h))
// ">"
ctx.move(to: CGPoint(x: mid + size * 0.10, y: mid + h))
ctx.addLine(to: CGPoint(x: mid + size * 0.10 + w, y: mid))
ctx.addLine(to: CGPoint(x: mid + size * 0.10, y: mid - h))
ctx.strokePath()
// small bar under the brackets
ctx.setStrokeColor(CGColor(red: 0.62, green: 0.66, blue: 0.72, alpha: 1))
ctx.setLineWidth(size * 0.035)
ctx.move(to: CGPoint(x: mid - size * 0.05, y: mid - h - size * 0.08))
ctx.addLine(to: CGPoint(x: mid + size * 0.05, y: mid - h - size * 0.08))
ctx.strokePath()

guard let image = ctx.makeImage() else { exit(1) }
let rep = NSBitmapImageRep(cgImage: image)
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: output))
