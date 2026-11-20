import AppKit
import Observation

/// Global settings (UserDefaults). Per-solution settings live in `.vslike/settings.json`.
@MainActor
@Observable
final class Preferences {
    static let shared = Preferences()

    enum Appearance: String, CaseIterable, Identifiable {
        case system, light, dark
        var id: String { rawValue }
    }

    enum Language: String, CaseIterable, Identifiable {
        case system, en, de
        var id: String { rawValue }
    }

    private let defaults: UserDefaults

    var styleCopByDefault: Bool { didSet { defaults.set(styleCopByDefault, forKey: "styleCopByDefault") } }
    var fontSize: Double { didSet { defaults.set(fontSize, forKey: "fontSize") } }
    var showMinimapOfDiagnostics: Bool { didSet { defaults.set(showMinimapOfDiagnostics, forKey: "showDiagnosticStripe") } }
    var autoCloseBrackets: Bool { didSet { defaults.set(autoCloseBrackets, forKey: "autoCloseBrackets") } }
    var formatOnSave: Bool { didSet { defaults.set(formatOnSave, forKey: "formatOnSave") } }
    var appearance: Appearance {
        didSet {
            defaults.set(appearance.rawValue, forKey: "appearance")
            applyAppearance()
        }
    }

    /// UI language; takes effect after a restart (macOS picks the localization at launch).
    var language: Language {
        didSet {
            defaults.set(language.rawValue, forKey: "language")
            if language == .system {
                defaults.removeObject(forKey: "AppleLanguages")
            } else {
                defaults.set([language.rawValue], forKey: "AppleLanguages")
            }
        }
    }

    var recentSolutions: [String] { didSet { defaults.set(recentSolutions, forKey: "recentSolutions") } }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        styleCopByDefault = defaults.object(forKey: "styleCopByDefault") as? Bool ?? false
        fontSize = defaults.object(forKey: "fontSize") as? Double ?? 13
        showMinimapOfDiagnostics = defaults.object(forKey: "showDiagnosticStripe") as? Bool ?? true
        autoCloseBrackets = defaults.object(forKey: "autoCloseBrackets") as? Bool ?? true
        formatOnSave = defaults.object(forKey: "formatOnSave") as? Bool ?? false
        appearance = Appearance(rawValue: defaults.string(forKey: "appearance") ?? "") ?? .system
        language = Language(rawValue: defaults.string(forKey: "language") ?? "") ?? .system
        recentSolutions = defaults.stringArray(forKey: "recentSolutions") ?? []
    }

    var editorFont: NSFont { NSFont.monospacedSystemFont(ofSize: fontSize, weight: .regular) }

    /// Culture for Roslyn messages and the dotnet CLI ("de" or "en").
    var effectiveLanguage: String {
        if language != .system {
            return language.rawValue
        }
        return Locale.preferredLanguages.first?.hasPrefix("de") == true ? "de" : "en"
    }

    func noteRecent(_ path: String) {
        recentSolutions = [path] + recentSolutions.filter { $0 != path }.prefix(9)
    }

    func applyAppearance() {
        switch appearance {
        case .system: NSApp?.appearance = nil
        case .light: NSApp?.appearance = NSAppearance(named: .aqua)
        case .dark: NSApp?.appearance = NSAppearance(named: .darkAqua)
        }
    }
}
