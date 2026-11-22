import Foundation

/// Finds the `dotnet` host. Apps started from Finder do not get the shell's `PATH`, so the usual install locations are
/// checked explicitly.
public enum DotnetLocator {
    public static let minimumMajorVersion = 10

    public static func locate(environment: [String: String] = ProcessInfo.processInfo.environment) -> URL? {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        var candidates: [String] = []
        if let root = environment["DOTNET_ROOT"], !root.isEmpty {
            candidates.append(root + "/dotnet")
        }
        candidates += (environment["PATH"] ?? "").split(separator: ":").map { $0 + "/dotnet" }
        candidates += [
            "/usr/local/share/dotnet/dotnet",
            "/opt/homebrew/bin/dotnet",
            "/usr/local/bin/dotnet",
            home + "/.dotnet/dotnet",
        ]
        return candidates
            .map { URL(fileURLWithPath: $0).resolvingSymlinksInPath() }
            .first { FileManager.default.isExecutableFile(atPath: $0.path) }
    }

    /// Parses `dotnet --list-sdks` (e.g. `10.0.402 [/usr/local/share/dotnet/sdk]`), newest first.
    public static func parseSdks(_ output: String) -> [SdkInfo] {
        output.split(separator: "\n").compactMap { line -> SdkInfo? in
            let parts = line.split(separator: " ", maxSplits: 1)
            guard let full = parts.first, let version = SemanticVersion(String(full)) else {
                return nil
            }
            let path = parts.count > 1 ? parts[1].trimmingCharacters(in: CharacterSet(charactersIn: "[] ")) : ""
            return SdkInfo(version: version, fullVersion: String(full), path: path)
        }
        .sorted { $0.version > $1.version }
    }

    /// One target framework (`net10.0`) per installed SDK major.minor, newest first, with the SDKs that provide it.
    public static func targetFrameworks(for sdks: [SdkInfo]) -> [TargetFrameworkOption] {
        var options: [TargetFrameworkOption] = []
        for sdk in sdks.sorted(by: { $0.version > $1.version }) {
            let moniker = "net\(sdk.version.major).\(sdk.version.minor)"
            if let index = options.firstIndex(where: { $0.moniker == moniker }) {
                options[index] = TargetFrameworkOption(moniker: moniker, sdkVersions: options[index].sdkVersions + [sdk.fullVersion])
            } else {
                options.append(TargetFrameworkOption(moniker: moniker, sdkVersions: [sdk.fullVersion]))
            }
        }
        return options
    }

    public static func listSdks(runner: CommandRunner) async -> [SdkInfo] {
        guard let result = try? await runner.run(["--list-sdks"]), result.succeeded else {
            return []
        }
        return parseSdks(result.standardOutput)
    }
}

public struct SdkInfo: Sendable, Hashable {
    public let version: SemanticVersion
    public let fullVersion: String
    public let path: String

    public var isSupported: Bool { version.major >= DotnetLocator.minimumMajorVersion }
}

/// A target framework moniker such as `net10.0` and the installed SDK versions that provide it (newest first).
public struct TargetFrameworkOption: Sendable, Hashable, Identifiable {
    public let moniker: String
    public let sdkVersions: [String]

    public var id: String { moniker }

    public init(moniker: String, sdkVersions: [String]) {
        self.moniker = moniker
        self.sdkVersions = sdkVersions
    }
}

/// `major.minor.patch` with an optional pre-release suffix (ignored for ordering of equal numbers).
public struct SemanticVersion: Sendable, Hashable, Comparable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int
    public let preRelease: String?

    public init?(_ text: String) {
        let main = text.split(separator: "-", maxSplits: 1)
        let numbers = main[0].split(separator: ".").map { Int($0) }
        guard numbers.count >= 2, numbers.allSatisfy({ $0 != nil }) else {
            return nil
        }
        major = numbers[0]!
        minor = numbers[1]!
        patch = numbers.count > 2 ? numbers[2]! : 0
        preRelease = main.count > 1 ? String(main[1]) : nil
    }

    public var description: String { "\(major).\(minor).\(patch)" + (preRelease.map { "-" + $0 } ?? "") }

    public static func < (lhs: SemanticVersion, rhs: SemanticVersion) -> Bool {
        if (lhs.major, lhs.minor, lhs.patch) != (rhs.major, rhs.minor, rhs.patch) {
            return (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
        }
        // A release is newer than its pre-releases.
        switch (lhs.preRelease, rhs.preRelease) {
        case (nil, nil): return false
        case (nil, _): return false
        case (_, nil): return true
        case let (l?, r?): return l < r
        }
    }
}
