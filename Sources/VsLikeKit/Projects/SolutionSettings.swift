import Foundation

/// Per-solution settings in `<solution folder>/.vslike/settings.json`. Written only when the user changes something.
public struct SolutionSettings: Codable, Sendable, Hashable {
    /// nil means "use the global default".
    public var styleCopEnabled: Bool?
    public var startupProject: String?
    public var configuration: String?
    public var launchProfile: String?
    public var breakpoints: [String: [SourceBreakpoint]]?

    public init() {}

    public static func file(for solution: URL) -> URL {
        solution.deletingLastPathComponent().appendingPathComponent(".vslike/settings.json")
    }

    public static func load(for solution: URL) -> SolutionSettings {
        guard let data = try? Data(contentsOf: file(for: solution)) else {
            return SolutionSettings()
        }
        return (try? JSONDecoder().decode(SolutionSettings.self, from: data)) ?? SolutionSettings()
    }

    public func save(for solution: URL) throws {
        let url = Self.file(for: solution)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        try encoder.encode(self).write(to: url, options: .atomic)
    }
}
