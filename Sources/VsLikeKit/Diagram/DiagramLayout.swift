import CoreGraphics
import Foundation

/// Position of one type on a diagram. `x`/`y` are NaN for a type that still has to be placed by `AutoLayout`.
public struct TypePlacement: Codable, Sendable, Hashable {
    public let fullName: String
    public let x: Double
    public let y: Double
    public let collapsed: Bool

    public init(fullName: String, x: Double, y: Double, collapsed: Bool = false) {
        self.fullName = fullName
        self.x = x
        self.y = y
        self.collapsed = collapsed
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        fullName = try c.decode(String.self, forKey: .fullName)
        x = try c.decodeIfPresent(Double.self, forKey: .x) ?? .nan
        y = try c.decodeIfPresent(Double.self, forKey: .y) ?? .nan
        collapsed = try c.decodeIfPresent(Bool.self, forKey: .collapsed) ?? false
    }

    public var isPlaced: Bool { !x.isNaN && !y.isNaN }

    public func moved(x: Double, y: Double) -> TypePlacement { TypePlacement(fullName: fullName, x: x, y: y, collapsed: collapsed) }

    public func toggled() -> TypePlacement { TypePlacement(fullName: fullName, x: x, y: y, collapsed: !collapsed) }
}

/// The layout file `<Name>.cdiagram.json`: which types are shown where, plus view settings. Types and members always
/// come from the code; this file holds no code information.
public struct DiagramDocument: Codable, Sendable, Hashable {
    public static let fileExtension = ".cdiagram.json"

    public var version: Int = 1
    public var types: [TypePlacement] = []
    public var zoom: Double = 1
    public var offsetX: Double = 0
    public var offsetY: Double = 0
    public var showGrid = true
    public var snapToGrid = true

    public init(types: [TypePlacement] = []) {
        self.types = types
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = try c.decodeIfPresent(Int.self, forKey: .version) ?? 1
        types = try c.decodeIfPresent([TypePlacement].self, forKey: .types) ?? []
        zoom = try c.decodeIfPresent(Double.self, forKey: .zoom) ?? 1
        offsetX = try c.decodeIfPresent(Double.self, forKey: .offsetX) ?? 0
        offsetY = try c.decodeIfPresent(Double.self, forKey: .offsetY) ?? 0
        showGrid = try c.decodeIfPresent(Bool.self, forKey: .showGrid) ?? true
        snapToGrid = try c.decodeIfPresent(Bool.self, forKey: .snapToGrid) ?? true
    }

    public static func name(of url: URL) -> String {
        let file = url.lastPathComponent
        return file.hasSuffix(fileExtension) ? String(file.dropLast(fileExtension.count)) : file
    }

    public static func load(_ url: URL) -> DiagramDocument {
        guard let data = try? Data(contentsOf: url) else {
            return DiagramDocument()
        }
        let decoder = JSONDecoder()
        decoder.nonConformingFloatDecodingStrategy = .convertFromString(positiveInfinity: "Infinity", negativeInfinity: "-Infinity", nan: "NaN")
        return (try? decoder.decode(DiagramDocument.self, from: data)) ?? DiagramDocument()
    }

    public func jsonData() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        encoder.nonConformingFloatEncodingStrategy = .convertToString(positiveInfinity: "Infinity", negativeInfinity: "-Infinity", nan: "NaN")
        var data = try encoder.encode(self)
        data.append(UInt8(ascii: "\n"))
        return data
    }

    public func save(to url: URL) throws {
        try jsonData().write(to: url, options: .atomic)
    }

    public func find(_ fullName: String) -> TypePlacement? { types.first { $0.fullName == fullName } }

    public func with(_ placement: TypePlacement) -> DiagramDocument {
        var copy = self
        if let index = copy.types.firstIndex(where: { $0.fullName == placement.fullName }) {
            copy.types[index] = placement
        } else {
            copy.types.append(placement)
        }
        return copy
    }

    public func without(_ fullName: String) -> DiagramDocument {
        var copy = self
        copy.types.removeAll { $0.fullName == fullName }
        return copy
    }

    public func renamed(_ oldName: String, to newName: String) -> DiagramDocument {
        var copy = self
        copy.types = types.map { $0.fullName == oldName ? TypePlacement(fullName: newName, x: $0.x, y: $0.y, collapsed: $0.collapsed) : $0 }
        return copy
    }
}

/// Box sizes in diagram coordinates. Member lines use a monospaced font, so sizes depend only on character counts and
/// are identical on the canvas, in hit testing and in the exports.
public enum BoxMetrics {
    public static let fontSize = 12.0
    public static let charWidth = fontSize * 0.6
    public static let lineHeight = 18.0
    public static let headerHeight = 44.0
    public static let padding = 10.0
    public static let minWidth = 160.0
    public static let gridSize = 20.0

    public static func lines(_ type: DiagramType, collapsed: Bool) -> [String] {
        collapsed || type.isExternal ? [] : type.members.map(\.umlLine)
    }

    public static func size(_ type: DiagramType, collapsed: Bool) -> CGSize {
        let lines = lines(type, collapsed: collapsed)
        let longest = lines.map(\.count).max() ?? 0
        let header = max(Double(type.displayName.count) * charWidth * 1.2, Double(type.stereotype.count) * charWidth)
        let width = max(minWidth, max(header, Double(longest) * charWidth) + 2 * padding + 8)
        let height = headerHeight + (lines.isEmpty ? 0 : Double(lines.count) * lineHeight + padding)
        return CGSize(width: snap(width, up: true), height: height)
    }

    public static func snap(_ value: Double, up: Bool = false) -> Double {
        up ? (value / gridSize).rounded(.up) * gridSize : (value / gridSize).rounded() * gridSize
    }
}

/// A type box on the canvas.
public struct DiagramBox: Sendable {
    public let type: DiagramType
    public let frame: CGRect
    public let collapsed: Bool

    public var center: CGPoint { CGPoint(x: frame.midX, y: frame.midY) }

    /// Point where the line from the center towards `point` leaves the box.
    public func border(towards point: CGPoint) -> CGPoint {
        let dx = point.x - center.x
        let dy = point.y - center.y
        if abs(dx) < 0.001 && abs(dy) < 0.001 {
            return center
        }
        let sx = abs(dx) < 0.001 ? Double.greatestFiniteMagnitude : frame.width / 2 / abs(dx)
        let sy = abs(dy) < 0.001 ? Double.greatestFiniteMagnitude : frame.height / 2 / abs(dy)
        let scale = min(sx, sy)
        return CGPoint(x: center.x + dx * scale, y: center.y + dy * scale)
    }

    /// Index of the member line at a point inside the box, or nil for the header.
    public func memberIndex(at point: CGPoint) -> Int? {
        let offset = point.y - frame.minY - BoxMetrics.headerHeight
        guard !collapsed, offset >= 0 else {
            return nil
        }
        let index = Int(offset / BoxMetrics.lineHeight)
        return index < type.members.count ? index : nil
    }
}

public struct DiagramEdge: Sendable {
    public let relation: DiagramRelation
    public let start: CGPoint
    public let end: CGPoint
}

public enum DiagramGeometry {
    public static func boxes(model: DiagramModel, document: DiagramDocument) -> [DiagramBox] {
        let byName = Dictionary(model.types.map { ($0.fullName, $0) }, uniquingKeysWith: { first, _ in first })
        return document.types.compactMap { placement in
            guard placement.isPlaced, let type = byName[placement.fullName] else {
                return nil
            }
            let size = BoxMetrics.size(type, collapsed: placement.collapsed)
            return DiagramBox(type: type, frame: CGRect(x: placement.x, y: placement.y, width: size.width, height: size.height), collapsed: placement.collapsed)
        }
    }

    /// Relations whose both ends are on the diagram, as border-to-border segments.
    public static func edges(model: DiagramModel, boxes: [DiagramBox]) -> [DiagramEdge] {
        let byName = Dictionary(boxes.map { ($0.type.fullName, $0) }, uniquingKeysWith: { first, _ in first })
        return model.relations.compactMap { relation in
            guard relation.from != relation.to, let from = byName[relation.from], let to = byName[relation.to] else {
                return nil
            }
            return DiagramEdge(relation: relation, start: from.border(towards: to.center), end: to.border(towards: from.center))
        }
    }

    public static func bounds(_ boxes: [DiagramBox], margin: Double = 20) -> CGRect {
        guard let first = boxes.first else {
            return .zero
        }
        return boxes.dropFirst().reduce(first.frame) { $0.union($1.frame) }.insetBy(dx: -margin, dy: -margin)
    }
}

/// Layered layout: base types and interfaces above the types that derive from them. Existing positions stay; only
/// unplaced types are arranged (or all of them with `all`).
public enum AutoLayout {
    static let horizontalGap = 60.0
    static let verticalGap = 80.0

    public static func arrange(_ document: DiagramDocument, model: DiagramModel, all: Bool) -> DiagramDocument {
        let shown = Set(document.types.map(\.fullName))
        let types = model.types.filter { shown.contains($0.fullName) }
        let toPlace = all ? types : types.filter { document.find($0.fullName)?.isPlaced != true }
        guard !toPlace.isEmpty else {
            return document
        }

        var parents: [String: [String]] = [:]
        for relation in model.relations where (relation.kind == .inheritance || relation.kind == .implementation) && shown.contains(relation.from) && shown.contains(relation.to) {
            parents[relation.from, default: []].append(relation.to)
        }
        var depthCache: [String: Int] = [:]
        func depth(_ name: String, guard level: Int) -> Int {
            if let known = depthCache[name] {
                return known
            }
            let value = level > 32 ? 0 : (parents[name] ?? []).map { depth($0, guard: level + 1) + 1 }.max() ?? 0
            depthCache[name] = value
            return value
        }

        let placing = Set(toPlace.map(\.fullName))
        let placed = all ? [] : document.types.filter { $0.isPlaced && !placing.contains($0.fullName) }
        var y = placed.isEmpty
            ? BoxMetrics.gridSize
            : placed.map { p in p.y + BoxMetrics.size(model.find(p.fullName) ?? DiagramType(fullName: p.fullName, name: p.fullName, kind: .class), collapsed: p.collapsed).height }.max()! + verticalGap
        var result = document
        let layers = Dictionary(grouping: toPlace) { depth($0.fullName, guard: 0) }
        for key in layers.keys.sorted() {
            var x = BoxMetrics.gridSize
            var rowHeight = 0.0
            for type in layers[key]!.sorted(by: { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }) {
                let collapsed = document.find(type.fullName)?.collapsed ?? false
                let size = BoxMetrics.size(type, collapsed: collapsed)
                result = result.with(TypePlacement(fullName: type.fullName, x: BoxMetrics.snap(x), y: BoxMetrics.snap(y), collapsed: collapsed))
                x += size.width + horizontalGap
                rowHeight = max(rowHeight, size.height)
            }
            y += rowHeight + verticalGap
        }
        return result
    }
}
