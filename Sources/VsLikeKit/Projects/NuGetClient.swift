import Foundation

public struct NuGetPackage: Sendable, Hashable, Identifiable {
    public let id: String
    public let latestVersion: String
    public let description: String?
    public let authors: String?
    public let totalDownloads: Int
    public let isVerified: Bool
    public let projectURL: String?
}

/// Search and version lookup against the NuGet v3 API (nuget.org by default). Sends no user data.
public actor NuGetClient {
    public static let defaultServiceIndex = URL(string: "https://api.nuget.org/v3/index.json")!

    private let session: URLSession
    private let serviceIndex: URL
    private var endpoints: (search: URL, packageBase: URL)?

    public init(session: URLSession = .shared, serviceIndex: URL = NuGetClient.defaultServiceIndex) {
        self.session = session
        self.serviceIndex = serviceIndex
    }

    public func search(_ query: String, includePrerelease: Bool = false, skip: Int = 0, take: Int = 30) async throws -> [NuGetPackage] {
        var components = URLComponents(url: try await resolveEndpoints().search, resolvingAgainstBaseURL: false)!
        components.queryItems = [
            URLQueryItem(name: "q", value: query),
            URLQueryItem(name: "skip", value: String(skip)),
            URLQueryItem(name: "take", value: String(take)),
            URLQueryItem(name: "prerelease", value: includePrerelease ? "true" : "false"),
            URLQueryItem(name: "semVerLevel", value: "2.0.0"),
        ]
        let root = try await json(components.url!)
        return (root["data"] as? [[String: Any]] ?? []).map(Self.package)
    }

    public func versions(of packageID: String, includePrerelease: Bool = false) async throws -> [String] {
        let url = try await resolveEndpoints().packageBase.appendingPathComponent(packageID.lowercased()).appendingPathComponent("index.json")
        let versions = try await json(url)["versions"] as? [String] ?? []
        return versions.filter { includePrerelease || !$0.contains("-") }.reversed()
    }

    static func package(_ item: [String: Any]) -> NuGetPackage {
        let authors = (item["authors"] as? [String])?.joined(separator: ", ") ?? item["authors"] as? String
        return NuGetPackage(
            id: item["id"] as? String ?? "",
            latestVersion: item["version"] as? String ?? "",
            description: item["description"] as? String,
            authors: authors,
            totalDownloads: (item["totalDownloads"] as? NSNumber)?.intValue ?? 0,
            isVerified: item["verified"] as? Bool ?? false,
            projectURL: item["projectUrl"] as? String
        )
    }

    private func resolveEndpoints() async throws -> (search: URL, packageBase: URL) {
        if let endpoints {
            return endpoints
        }
        let resources = try await json(serviceIndex)["resources"] as? [[String: Any]] ?? []
        func find(_ prefix: String) -> URL? {
            resources.first { ($0["@type"] as? String)?.hasPrefix(prefix) == true }.flatMap { ($0["@id"] as? String).flatMap(URL.init(string:)) }
        }
        guard let search = find("SearchQueryService"), let packageBase = find("PackageBaseAddress") else {
            throw URLError(.cannotParseResponse)
        }
        endpoints = (search, packageBase)
        return (search, packageBase)
    }

    private func json(_ url: URL) async throws -> [String: Any] {
        var request = URLRequest(url: url)
        request.setValue("VsLike-IDE", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw URLError(.badServerResponse)
        }
        return try JSONSerialization.jsonObject(with: data) as? [String: Any] ?? [:]
    }
}
