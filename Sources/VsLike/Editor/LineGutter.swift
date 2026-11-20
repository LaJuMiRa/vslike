import AppKit
import VsLikeKit

/// Line numbers, breakpoints (click to toggle, right-click for a condition), the execution arrow and diagnostic
/// marks to the left of the text.
@MainActor
final class LineGutter: NSRulerView {
    private weak var codeView: CodeTextView?
    var onToggleBreakpoint: ((Int) -> Void)?
    var onEditCondition: ((Int) -> Void)?

    init(textView: CodeTextView) {
        codeView = textView
        super.init(scrollView: textView.enclosingScrollView, orientation: .verticalRuler)
        clientView = textView
        ruleThickness = 52
    }

    @available(*, unavailable)
    required init(coder: NSCoder) {
        fatalError("not supported")
    }

    override var isFlipped: Bool { true }

    func updateThickness(lineCount: Int) {
        let digits = max(3, String(lineCount).count)
        let font = NSFont.monospacedDigitSystemFont(ofSize: max(10, (codeView?.font?.pointSize ?? 13) - 2), weight: .regular)
        let width = CGFloat(digits) * ("8" as NSString).size(withAttributes: [.font: font]).width + 34
        if abs(ruleThickness - width) > 0.5 {
            ruleThickness = width
        }
    }

    override func drawHashMarksAndLabels(in rect: NSRect) {
        guard let textView = codeView, let layoutManager = textView.layoutManager, let container = textView.textContainer, let document = textView.document else {
            return
        }
        Palette.editorBackground.setFill()
        bounds.fill()

        let font = NSFont.monospacedDigitSystemFont(ofSize: max(10, (textView.font?.pointSize ?? 13) - 2), weight: .regular)
        let string = textView.string as NSString
        let visible = textView.visibleRect
        let glyphs = layoutManager.glyphRange(forBoundingRect: visible, in: container)
        let characters = layoutManager.characterRange(forGlyphRange: glyphs, actualGlyphRange: nil)
        let caretLine = textView.caretLine
        let diagnosticLines = textView.diagnosticSeverityByLine
        let offsetY = convert(NSPoint.zero, from: textView).y + textView.textContainerInset.height

        var lineNumber = textView.lineNumber(at: characters.location)
        var index = string.lineRange(for: NSRange(location: min(characters.location, string.length), length: 0)).location
        let end = NSMaxRange(characters)
        while index < string.length && index <= end {
            let lineRange = string.lineRange(for: NSRange(location: index, length: 0))
            let glyph = layoutManager.glyphIndexForCharacter(at: index)
            let lineRect = layoutManager.lineFragmentRect(forGlyphAt: glyph, effectiveRange: nil, withoutAdditionalLayout: true)
            drawLine(lineNumber, y: lineRect.minY + offsetY, height: lineRect.height, font: font, isCaret: lineNumber == caretLine, document: document, severity: diagnosticLines[lineNumber])
            index = NSMaxRange(lineRange)
            lineNumber += 1
        }
        let extra = layoutManager.extraLineFragmentRect
        if extra.height > 0, extra.maxY + offsetY >= visible.minY - 40 {
            drawLine(textView.lineCount, y: extra.minY + offsetY, height: extra.height, font: font, isCaret: textView.lineCount == caretLine, document: document, severity: nil)
        }
    }

    private func drawLine(_ line: Int, y: CGFloat, height: CGFloat, font: NSFont, isCaret: Bool, document: CodeDocument, severity: DiagnosticSeverity?) {
        let markerSize = min(height - 4, 13)
        let markerRect = NSRect(x: 6, y: y + (height - markerSize) / 2, width: markerSize + 8, height: markerSize)
        if let breakpoint = document.breakpoints[line] {
            let path = NSBezierPath(roundedRect: markerRect, xRadius: markerSize / 2.6, yRadius: markerSize / 2.6)
            Palette.breakpoint.setFill()
            path.fill()
            if breakpoint.condition?.isEmpty == false {
                NSColor.white.setFill()
                NSBezierPath(ovalIn: NSRect(x: markerRect.midX - 2, y: markerRect.midY - 2, width: 4, height: 4)).fill()
            }
        } else if let severity, severity >= .warning {
            Palette.color(for: severity).withAlphaComponent(0.9).setFill()
            NSBezierPath(ovalIn: NSRect(x: 10, y: y + height / 2 - 3, width: 6, height: 6)).fill()
        }
        if document.executionLine == line {
            let arrow = NSBezierPath()
            let top = y + 2
            let bottom = y + height - 2
            arrow.move(to: NSPoint(x: 4, y: top))
            arrow.line(to: NSPoint(x: ruleThickness - 14, y: top))
            arrow.line(to: NSPoint(x: ruleThickness - 6, y: (top + bottom) / 2))
            arrow.line(to: NSPoint(x: ruleThickness - 14, y: bottom))
            arrow.line(to: NSPoint(x: 4, y: bottom))
            arrow.close()
            Palette.warning.withAlphaComponent(0.85).setFill()
            arrow.fill()
        }
        let attributes: [NSAttributedString.Key: Any] = [
            .font: isCaret ? NSFont.monospacedDigitSystemFont(ofSize: font.pointSize, weight: .semibold) : font,
            .foregroundColor: isCaret ? Palette.gutterActive : Palette.gutterText,
        ]
        let text = String(line) as NSString
        let size = text.size(withAttributes: attributes)
        text.draw(at: NSPoint(x: ruleThickness - size.width - 10, y: y + (height - size.height) / 2), withAttributes: attributes)
    }

    override func mouseDown(with event: NSEvent) {
        guard let line = line(at: event) else { return }
        if event.modifierFlags.contains(.control) {
            onEditCondition?(line)
        } else {
            onToggleBreakpoint?(line)
        }
    }

    override func rightMouseDown(with event: NSEvent) {
        guard let line = line(at: event) else { return }
        onEditCondition?(line)
    }

    private func line(at event: NSEvent) -> Int? {
        guard let textView = codeView, let layoutManager = textView.layoutManager, let container = textView.textContainer else { return nil }
        let point = textView.convert(event.locationInWindow, from: nil)
        let containerPoint = NSPoint(x: 0, y: point.y - textView.textContainerInset.height)
        let glyph = layoutManager.glyphIndex(for: containerPoint, in: container)
        let character = layoutManager.characterIndexForGlyph(at: glyph)
        if layoutManager.extraLineFragmentRect.height > 0, containerPoint.y >= layoutManager.extraLineFragmentRect.minY {
            return textView.lineCount
        }
        return textView.lineNumber(at: character)
    }
}

/// The vertical scroller with marks for errors, warnings and breakpoints over the whole file.
final class MarkerScroller: NSScroller {
    struct Mark {
        let fraction: CGFloat
        let color: NSColor
    }

    var marks: [Mark] = [] {
        didSet { needsDisplay = true }
    }

    override class var isCompatibleWithOverlayScrollers: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let track = rect(for: .knobSlot).insetBy(dx: 0, dy: 4)
        for mark in marks {
            let y = track.minY + mark.fraction * track.height
            mark.color.setFill()
            NSBezierPath(roundedRect: NSRect(x: track.minX + 2, y: y - 1.5, width: max(4, track.width - 4), height: 3), xRadius: 1.5, yRadius: 1.5).fill()
        }
    }
}
