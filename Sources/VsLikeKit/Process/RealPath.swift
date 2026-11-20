import Foundation

/// Canonical file paths. The debugger, MSBuild and Roslyn report real paths (`/private/var/…`), while Foundation's
/// `resolvingSymlinksInPath` strips `/private`; `realpath(3)` gives the form they use.
public enum RealPath {
    public static func resolve(_ path: String) -> String {
        guard let resolved = realpath(path, nil) else {
            return (path as NSString).standardizingPath
        }
        defer { free(resolved) }
        return String(cString: resolved)
    }

    public static func resolve(_ url: URL) -> URL { URL(fileURLWithPath: resolve(url.path)) }
}
