import Foundation

/// The startup hook (`engine/src/VsLike.DebugStartupHook`) that holds a program debugged in the terminal before
/// `Main` until the debugger has attached and set the breakpoints.
public enum DebugStartupHook {
    public static let readyFileVariable = "VSLIKE_DEBUG_READY_FILE"
    public static let fileName = "VsLike.DebugStartupHook.dll"

    /// `VSLIKE_DEBUG_HOOK`, the app bundle (`Contents/Resources/debughook/`), or the build of this repository.
    public static func locate(bundle: Bundle = .main) -> URL? {
        var candidates: [String] = []
        if let override = ProcessInfo.processInfo.environment["VSLIKE_DEBUG_HOOK"] {
            candidates.append(override)
        }
        if let resources = bundle.resourceURL {
            candidates.append(resources.appendingPathComponent("debughook/\(fileName)").path)
        }
        let repository = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        candidates.append(repository.appendingPathComponent("engine/artifacts.nosync/bin/VsLike.DebugStartupHook/debug/\(fileName)").path)
        return candidates.map(URL.init(fileURLWithPath:)).first { FileManager.default.fileExists(atPath: $0.path) }
    }

    /// The program's environment with the hook added (after any hooks the launch profile already sets).
    public static func environment(_ base: [String: String], hook: URL, readyFile: URL) -> [String: String] {
        var environment = base
        let existing = environment["DOTNET_STARTUP_HOOKS"].map { $0.isEmpty ? [] : [$0] } ?? []
        environment["DOTNET_STARTUP_HOOKS"] = (existing + [hook.path]).joined(separator: ":")
        environment[readyFileVariable] = readyFile.path
        return environment
    }
}
