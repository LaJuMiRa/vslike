import Foundation

/// Reads and writes class diagrams in the `.cd` XML format that C# IDEs keep in projects:
///
///     <ClassDiagram MajorVersion="1" MinorVersion="1">
///       <Class Name="Demo.Box&lt;TItem&gt;" Collapsed="true">
///         <Position X="0.5" Y="0.5" Width="1.5" />
///         <TypeIdentifier><HashCode>…</HashCode><FileName>Box.cs</FileName></TypeIdentifier>
///       </Class>
///     </ClassDiagram>
///
/// Positions are in inches. Only type positions and the collapsed state are read and written; everything else in an
/// existing file (comments, association settings, fonts) stays as it is.
public enum ClassDiagramFile {
    public static let fileExtension = "cd"

    /// What a new type element needs: its tag (`Class`, `Interface`, `Struct`, `Enum`), its name in the file
    /// (`Demo.Box<TItem>`) and its source file relative to the diagram (`Models\Box.cs`).
    public struct TypeInfo: Sendable, Hashable {
        public let tag: String
        public let name: String
        public let fileName: String

        public init(tag: String, name: String, fileName: String) {
            self.tag = tag
            self.name = name
            self.fileName = fileName
        }

        public init(_ type: DiagramType, diagramFolder: URL) {
            switch type.kind {
            case .interface: tag = "Interface"
            case .struct, .recordStruct: tag = "Struct"
            case .enum: tag = "Enum"
            case .class, .record: tag = "Class"
            }
            // `Demo.Outer.Box`1` becomes `Demo.Outer.Box<TItem>`.
            let plain = type.fullName.split(separator: "`").first.map(String.init) ?? type.fullName
            name = type.typeParameters.isEmpty ? plain : "\(plain)<\(type.typeParameters.map(\.name).joined(separator: ", "))>"
            let folder = diagramFolder.standardizedFileURL.path + "/"
            let path = type.filePaths.first ?? ""
            fileName = (path.hasPrefix(folder) ? String(path.dropFirst(folder.count)) : URL(fileURLWithPath: path).lastPathComponent)
                .replacingOccurrences(of: "/", with: "\\")
        }
    }

    /// Diagram coordinates per inch.
    static let pointsPerInch = 96.0
    static let typeTags: Set<String> = ["Class", "Interface", "Struct", "Enum", "Delegate"]
    static let placeholderHash = String(repeating: "A", count: 43) + "="

    public static func isClassDiagram(_ url: URL) -> Bool {
        url.pathExtension.lowercased() == fileExtension
    }

    public static func read(_ data: Data) throws -> DiagramDocument {
        let xml = try XMLDocument(data: data)
        var document = DiagramDocument()
        document.types = typeElements(of: xml).compactMap { element in
            guard let name = element.attribute(forName: "Name")?.stringValue else { return nil }
            let position = element.elements(forName: "Position").first
            let x = position?.attribute(forName: "X")?.stringValue.flatMap(Double.init).map { $0 * pointsPerInch } ?? .nan
            let y = position?.attribute(forName: "Y")?.stringValue.flatMap(Double.init).map { $0 * pointsPerInch } ?? .nan
            let collapsed = element.attribute(forName: "Collapsed")?.stringValue == "true"
            return TypePlacement(fullName: fullName(fromFileName: name), x: x, y: y, collapsed: collapsed)
        }
        return document
    }

    /// Writes the layout into `existing` (or a new file): positions of listed types are updated, new types are
    /// added, types no longer on the diagram are removed.
    public static func write(_ document: DiagramDocument, existing: Data?, types: [String: TypeInfo]) throws -> Data {
        let xml = try existing.map { try XMLDocument(data: $0) } ?? newDocument()
        guard let root = xml.rootElement() else { throw CocoaError(.fileReadCorruptFile) }
        var elements: [String: XMLElement] = [:]
        for element in typeElements(of: xml) {
            if let name = element.attribute(forName: "Name")?.stringValue {
                elements[fullName(fromFileName: name)] = element
            }
        }
        let shown = Set(document.types.map(\.fullName))
        for (name, element) in elements where !shown.contains(name) {
            element.detach()
        }
        for placement in document.types {
            // Types without a source file (external base types) cannot be listed in a .cd file.
            guard let element = elements[placement.fullName] ?? types[placement.fullName].map({ newElement($0, in: root) }) else {
                continue
            }
            update(element, with: placement)
        }
        return xml.xmlData(options: [.nodePrettyPrint, .nodeCompactEmptyElement])
    }

    /// `Demo.Box<TItem>` (file) to `Demo.Box`1` (diagram model), also for nested generics.
    public static func fullName(fromFileName name: String) -> String {
        var result = ""
        var depth = 0
        var arity = 1
        for character in name {
            switch character {
            case "<":
                depth += 1
                if depth == 1 { arity = 1 }
            case ">":
                depth -= 1
                if depth == 0 { result += "`\(arity)" }
            case "," where depth == 1:
                arity += 1
            default:
                if depth == 0 { result.append(character) }
            }
        }
        return result
    }

    private static func typeElements(of xml: XMLDocument) -> [XMLElement] {
        xml.rootElement()?.children?.compactMap { $0 as? XMLElement }.filter { typeTags.contains($0.name ?? "") } ?? []
    }

    private static func newDocument() -> XMLDocument {
        let root = XMLElement(name: "ClassDiagram")
        root.addAttribute(XMLNode.attribute(withName: "MajorVersion", stringValue: "1") as! XMLNode)
        root.addAttribute(XMLNode.attribute(withName: "MinorVersion", stringValue: "1") as! XMLNode)
        let xml = XMLDocument(rootElement: root)
        xml.version = "1.0"
        xml.characterEncoding = "utf-8"
        return xml
    }

    private static func newElement(_ info: TypeInfo, in root: XMLElement) -> XMLElement {
        let element = XMLElement(name: info.tag)
        element.setAttributesWith(["Name": info.name])
        let identifier = XMLElement(name: "TypeIdentifier")
        identifier.addChild(XMLElement(name: "HashCode", stringValue: placeholderHash))
        identifier.addChild(XMLElement(name: "FileName", stringValue: info.fileName))
        element.addChild(identifier)
        root.addChild(element)
        return element
    }

    private static func update(_ element: XMLElement, with placement: TypePlacement) {
        if placement.collapsed {
            element.removeAttribute(forName: "Collapsed")
            element.addAttribute(XMLNode.attribute(withName: "Collapsed", stringValue: "true") as! XMLNode)
        } else {
            element.removeAttribute(forName: "Collapsed")
        }
        guard placement.isPlaced else { return }
        let position = element.elements(forName: "Position").first ?? {
            let created = XMLElement(name: "Position")
            element.insertChild(created, at: 0)
            return created
        }()
        let width = position.attribute(forName: "Width")?.stringValue ?? "1.5"
        position.setAttributesWith([
            "X": inches(placement.x),
            "Y": inches(placement.y),
            "Width": width,
        ])
    }

    private static func inches(_ points: Double) -> String {
        let value = (points / pointsPerInch * 1000).rounded() / 1000
        return value == value.rounded() ? String(format: "%.1f", value) : String(value)
    }
}
