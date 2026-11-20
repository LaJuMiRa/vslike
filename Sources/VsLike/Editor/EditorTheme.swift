import AppKit
import SwiftUI
import VsLikeKit

/// Colours of VsLike's own look: slate surfaces with an amber accent (the app icon's colours). Every colour adapts to
/// light and dark appearance.
enum Palette {
    static let accent = NSColor(name: "accent") { $0.isDark ? NSColor(srgbRed: 0.95, green: 0.70, blue: 0.29, alpha: 1) : NSColor(srgbRed: 0.80, green: 0.52, blue: 0.08, alpha: 1) }
    static let editorBackground = NSColor(name: "editorBackground") { $0.isDark ? NSColor(srgbRed: 0.105, green: 0.115, blue: 0.135, alpha: 1) : NSColor(srgbRed: 0.995, green: 0.995, blue: 1, alpha: 1) }
    static let gutterText = NSColor(name: "gutterText") { $0.isDark ? NSColor(white: 1, alpha: 0.28) : NSColor(white: 0, alpha: 0.3) }
    static let gutterActive = NSColor(name: "gutterActive") { $0.isDark ? NSColor(white: 1, alpha: 0.75) : NSColor(white: 0, alpha: 0.7) }
    static let currentLine = NSColor(name: "currentLine") { $0.isDark ? NSColor(white: 1, alpha: 0.045) : NSColor(srgbRed: 0.2, green: 0.3, blue: 0.5, alpha: 0.05) }
    static let selection = NSColor(name: "selection") { $0.isDark ? NSColor(srgbRed: 0.95, green: 0.70, blue: 0.29, alpha: 0.22) : NSColor(srgbRed: 0.95, green: 0.70, blue: 0.29, alpha: 0.28) }
    static let bracketMatch = NSColor(name: "bracketMatch") { $0.isDark ? NSColor(white: 1, alpha: 0.14) : NSColor(white: 0, alpha: 0.1) }
    static let executionLine = NSColor(name: "executionLine") { $0.isDark ? NSColor(srgbRed: 0.95, green: 0.78, blue: 0.2, alpha: 0.18) : NSColor(srgbRed: 1, green: 0.85, blue: 0.2, alpha: 0.35) }
    static let breakpoint = NSColor(srgbRed: 0.90, green: 0.30, blue: 0.27, alpha: 1)
    static let error = NSColor(srgbRed: 0.93, green: 0.33, blue: 0.31, alpha: 1)
    static let warning = NSColor(srgbRed: 0.93, green: 0.68, blue: 0.20, alpha: 1)
    static let info = NSColor(srgbRed: 0.36, green: 0.62, blue: 0.95, alpha: 1)
    static let styleCop = NSColor(srgbRed: 0.33, green: 0.72, blue: 0.68, alpha: 1)
    static let success = NSColor(srgbRed: 0.30, green: 0.74, blue: 0.45, alpha: 1)

    static func color(for severity: DiagnosticSeverity) -> NSColor {
        switch severity {
        case .error: return error
        case .warning: return warning
        case .info, .hidden: return info
        }
    }
}

extension NSAppearance {
    var isDark: Bool { bestMatch(from: [.darkAqua, .aqua]) == .darkAqua }
}

extension NSColor {
    convenience init(name: String, dynamic: @escaping @Sendable (NSAppearance) -> NSColor) {
        self.init(name: NSColor.Name(name), dynamicProvider: dynamic)
    }
}

extension Color {
    static let vsAccent = Color(nsColor: Palette.accent)
    static let vsError = Color(nsColor: Palette.error)
    static let vsWarning = Color(nsColor: Palette.warning)
    static let vsInfo = Color(nsColor: Palette.info)
    static let vsStyleCop = Color(nsColor: Palette.styleCop)
    static let vsSuccess = Color(nsColor: Palette.success)

    static func severity(_ severity: DiagnosticSeverity) -> Color { Color(nsColor: Palette.color(for: severity)) }
}

/// Syntax colours for lexical tokens and Roslyn's semantic classifications.
enum SyntaxColors {
    private static func dynamic(_ name: String, dark: UInt32, light: UInt32) -> NSColor {
        NSColor(name: name) { $0.isDark ? NSColor(hex: dark) : NSColor(hex: light) }
    }

    static let text = dynamic("syntax.text", dark: 0xDCDFE6, light: 0x1F2329)
    static let keyword = dynamic("syntax.keyword", dark: 0xFF8FB1, light: 0xC2185B)
    static let type = dynamic("syntax.type", dark: 0x6CD4C4, light: 0x00796B)
    static let interface = dynamic("syntax.interface", dark: 0x9BE7C4, light: 0x2E7D5B)
    static let method = dynamic("syntax.method", dark: 0x82B8FF, light: 0x1E5FB8)
    static let member = dynamic("syntax.member", dark: 0xC0AEFF, light: 0x6A4BC4)
    static let local = dynamic("syntax.local", dark: 0xE8EAF0, light: 0x2A2F37)
    static let parameter = dynamic("syntax.parameter", dark: 0xF2D7A6, light: 0x7A4F12)
    static let string = dynamic("syntax.string", dark: 0xF2C97D, light: 0xA35A00)
    static let number = dynamic("syntax.number", dark: 0x9BE2A0, light: 0x2E7D32)
    static let comment = dynamic("syntax.comment", dark: 0x6F7787, light: 0x8A909A)
    static let preprocessor = dynamic("syntax.preprocessor", dark: 0xE8A06C, light: 0xB35A1F)
    static let namespace = dynamic("syntax.namespace", dark: 0xC8CDD6, light: 0x4A515C)
    static let punctuation = dynamic("syntax.punctuation", dark: 0xA9AFBB, light: 0x555B66)

    static func color(for kind: TokenKind) -> NSColor {
        switch kind {
        case .keyword, .controlKeyword, .typeKeyword: return keyword
        case .identifier: return text
        case .number: return number
        case .string, .character: return string
        case .comment, .docComment: return comment
        case .preprocessor: return preprocessor
        case .operator, .punctuation: return punctuation
        }
    }

    /// Colour for a Roslyn classification name (see SemanticClassifications in the engine), nil keeps the lexical one.
    static func color(forClassification name: String) -> NSColor? {
        switch name {
        case "class", "struct", "enum", "delegate", "record", "typeParameter": return type
        case "interface": return interface
        case "method", "extensionMethod": return method
        case "property", "field", "event", "constant", "enumMember": return member
        case "parameter": return parameter
        case "local": return local
        case "namespace": return namespace
        case "keyword": return keyword
        default: return nil
        }
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(
            srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: alpha
        )
    }
}
