import Foundation

/// A JSON value for building request parameters inline: `["path": .string(path), "offset": 12]`.
public enum JSONValue: Codable, Sendable, Hashable, ExpressibleByStringLiteral, ExpressibleByIntegerLiteral,
    ExpressibleByBooleanLiteral, ExpressibleByDictionaryLiteral, ExpressibleByArrayLiteral, ExpressibleByNilLiteral
{
    case string(String)
    case number(Double)
    case bool(Bool)
    case array([JSONValue])
    case object([String: JSONValue])
    case null

    public init(stringLiteral value: String) { self = .string(value) }

    public init(integerLiteral value: Int) { self = .number(Double(value)) }

    public init(booleanLiteral value: Bool) { self = .bool(value) }

    public init(dictionaryLiteral elements: (String, JSONValue)...) {
        self = .object(Dictionary(elements, uniquingKeysWith: { $1 }))
    }

    public init(arrayLiteral elements: JSONValue...) { self = .array(elements) }

    public init(nilLiteral: ()) { self = .null }

    public static func int(_ value: Int) -> JSONValue { .number(Double(value)) }

    public static func optional(_ value: String?) -> JSONValue { value.map { .string($0) } ?? .null }

    public static func strings(_ values: [String]) -> JSONValue { .array(values.map { .string($0) }) }

    /// Wraps any `Encodable` value (encoded with the protocol's settings).
    public static func encode(_ value: some Encodable) throws -> JSONValue {
        let data = try JSONEncoder().encode(value)
        return try JSONDecoder().decode(JSONValue.self, from: data)
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([JSONValue].self) {
            self = .array(value)
        } else {
            self = .object(try container.decode([String: JSONValue].self))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case let .string(value): try container.encode(value)
        case let .number(value):
            if value.rounded() == value, abs(value) < 1e15 {
                try container.encode(Int(value))
            } else {
                try container.encode(value)
            }
        case let .bool(value): try container.encode(value)
        case let .array(value): try container.encode(value)
        case let .object(value): try container.encode(value)
        case .null: try container.encodeNil()
        }
    }

    public subscript(key: String) -> JSONValue? {
        if case let .object(object) = self { return object[key] }
        return nil
    }

    public var stringValue: String? {
        if case let .string(value) = self { return value }
        return nil
    }

    public var intValue: Int? {
        if case let .number(value) = self { return Int(value) }
        return nil
    }

    public var boolValue: Bool? {
        if case let .bool(value) = self { return value }
        return nil
    }

    public var arrayValue: [JSONValue] {
        if case let .array(value) = self { return value }
        return []
    }

    /// Decodes this value into a `Decodable` type.
    public func decode<T: Decodable>(_ type: T.Type) throws -> T {
        try JSONDecoder().decode(T.self, from: JSONEncoder().encode(self))
    }
}
