import Foundation

/// A `.csproj` file: reads and writes unconditional properties and lists references, keeping the rest of the file.
public struct ProjectFile: Sendable {
    public static let editableProperties = [
        "TargetFramework", "OutputType", "RootNamespace", "AssemblyName", "Nullable", "ImplicitUsings", "LangVersion",
        "TreatWarningsAsErrors", "Version", "Authors", "Description", "GenerateDocumentationFile", "InvariantGlobalization",
    ]

    public let path: String
    public private(set) var text: String

    public init(path: String, text: String) {
        self.path = path
        self.text = text
    }

    public static func load(_ path: String) throws -> ProjectFile {
        ProjectFile(path: path, text: try String(contentsOfFile: path, encoding: .utf8))
    }

    public var name: String { URL(fileURLWithPath: path).deletingPathExtension().lastPathComponent }

    public var directory: URL { URL(fileURLWithPath: path).deletingLastPathComponent() }

    /// The last value of an unconditional `<PropertyGroup>` property.
    public func property(_ name: String) -> String? {
        unconditionalGroups().flatMap { propertyMatches(name, in: $0) }.last.map { String(text[$0.value]) }
    }

    /// Sets (or with nil/empty removes) a property, editing only that element's text.
    public mutating func setProperty(_ name: String, _ value: String?) {
        let escaped = (value ?? "").replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
        let groups = unconditionalGroups()
        if let existing = groups.flatMap({ propertyMatches(name, in: $0) }).last {
            if escaped.isEmpty {
                var start = existing.element.lowerBound
                while start > text.startIndex, text[text.index(before: start)] == " " || text[text.index(before: start)] == "\t" {
                    start = text.index(before: start)
                }
                if start > text.startIndex, text[text.index(before: start)] == "\n" {
                    start = text.index(before: start)
                }
                text.removeSubrange(start..<existing.element.upperBound)
            } else {
                text.replaceSubrange(existing.value, with: escaped)
            }
            return
        }
        guard !escaped.isEmpty else {
            return
        }
        if let group = groups.first, let close = text.range(of: "</PropertyGroup>", range: group) {
            var lineStart = close.lowerBound
            while lineStart > text.startIndex, text[text.index(before: lineStart)] != "\n" {
                lineStart = text.index(before: lineStart)
            }
            let closingIndent = String(text[lineStart..<close.lowerBound])
            text.insert(contentsOf: "  \(closingIndent)<\(name)>\(escaped)</\(name)>\n", at: lineStart)
        } else if let project = text.range(of: #"<Project[^>]*>"#, options: .regularExpression) {
            text.insert(contentsOf: "\n  <PropertyGroup>\n    <\(name)>\(escaped)</\(name)>\n  </PropertyGroup>\n", at: project.upperBound)
        }
    }

    public func projectReferences() -> [String] {
        attributeValues(element: "ProjectReference", attribute: "Include").map {
            directory.appendingPathComponent($0.replacingOccurrences(of: "\\", with: "/")).standardizedFileURL.path
        }
    }

    public func packageReferences() -> [(id: String, version: String?)] {
        guard let document = try? XMLDocument(xmlString: text) else {
            return []
        }
        let nodes = (try? document.nodes(forXPath: "//*[local-name()='PackageReference']")) ?? []
        return nodes.compactMap { node in
            guard let element = node as? XMLElement, let id = element.attribute(forName: "Include")?.stringValue, !id.isEmpty else {
                return nil
            }
            let version = element.attribute(forName: "Version")?.stringValue ?? element.elements(forName: "Version").first?.stringValue
            return (id, version)
        }
    }

    public var isTestProject: Bool {
        let testPackages = ["microsoft.net.test.sdk", "mstest.sdk", "xunit.v3", "tunit"]
        return property("IsTestProject")?.lowercased() == "true"
            || packageReferences().contains { testPackages.contains($0.id.lowercased()) }
            || text.range(of: "Sdk=\"MSTest.Sdk", options: .caseInsensitive) != nil
    }

    public func save() throws {
        try text.write(toFile: path, atomically: true, encoding: .utf8)
    }

    private func attributeValues(element: String, attribute: String) -> [String] {
        guard let document = try? XMLDocument(xmlString: text) else {
            return []
        }
        let nodes = (try? document.nodes(forXPath: "//*[local-name()='\(element)']")) ?? []
        return nodes.compactMap { ($0 as? XMLElement)?.attribute(forName: attribute)?.stringValue }
    }

    /// Ranges of `<PropertyGroup>` blocks without a `Condition` attribute.
    private func unconditionalGroups() -> [Range<String.Index>] {
        var result: [Range<String.Index>] = []
        var searchStart = text.startIndex
        while let open = text.range(of: #"<PropertyGroup(\s[^>]*)?>"#, options: .regularExpression, range: searchStart..<text.endIndex) {
            guard let close = text.range(of: "</PropertyGroup>", range: open.upperBound..<text.endIndex) else {
                break
            }
            if !text[open].contains("Condition") {
                result.append(open.lowerBound..<close.upperBound)
            }
            searchStart = close.upperBound
        }
        return result
    }

    private func propertyMatches(_ name: String, in group: Range<String.Index>) -> [(element: Range<String.Index>, value: Range<String.Index>)] {
        var matches: [(Range<String.Index>, Range<String.Index>)] = []
        var start = group.lowerBound
        while let open = text.range(of: "<\(name)>", range: start..<group.upperBound),
              let close = text.range(of: "</\(name)>", range: open.upperBound..<group.upperBound) {
            matches.append((open.lowerBound..<close.upperBound, open.upperBound..<close.lowerBound))
            start = close.upperBound
        }
        return matches
    }
}
