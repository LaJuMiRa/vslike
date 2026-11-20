import Foundation

// The class diagram model as the engine reads it from the code (engine/src/VsLike.Diagram/Model).

public enum Access: String, Codable, Sendable, CaseIterable {
    case `public`, `internal`, protected, protectedInternal, privateProtected, `private`

    public var keyword: String {
        switch self {
        case .public: return "public"
        case .internal: return "internal"
        case .protected: return "protected"
        case .protectedInternal: return "protected internal"
        case .privateProtected: return "private protected"
        case .private: return "private"
        }
    }

    /// UML symbol: `+` public, `#` protected, `~` internal, `-` private.
    public var symbol: String {
        switch self {
        case .public: return "+"
        case .protected, .protectedInternal, .privateProtected: return "#"
        case .internal: return "~"
        case .private: return "-"
        }
    }
}

public enum DiagramTypeKind: String, Codable, Sendable, CaseIterable {
    case `class`, interface, `struct`, `enum`, record, recordStruct

    public var keyword: String { self == .recordStruct ? "record struct" : rawValue }
}

public struct TypeModifiers: OptionSet, Codable, Sendable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let abstract = TypeModifiers(rawValue: 1)
    public static let `static` = TypeModifiers(rawValue: 2)
    public static let sealed = TypeModifiers(rawValue: 4)
    public static let partial = TypeModifiers(rawValue: 8)
}

public struct MemberModifiers: OptionSet, Codable, Sendable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let `static` = MemberModifiers(rawValue: 1)
    public static let abstract = MemberModifiers(rawValue: 2)
    public static let virtual = MemberModifiers(rawValue: 4)
    public static let override = MemberModifiers(rawValue: 8)
    public static let readOnly = MemberModifiers(rawValue: 16)
    public static let const = MemberModifiers(rawValue: 32)
    public static let async = MemberModifiers(rawValue: 64)
    public static let required = MemberModifiers(rawValue: 128)
}

public enum MemberKind: String, Codable, Sendable, CaseIterable {
    case field, property, method, constructor, event, enumValue
}

public enum PropertyAccessors: String, Codable, Sendable {
    case getSet, getInit, getOnly, getPrivateSet
}

public enum RelationKind: String, Codable, Sendable {
    case inheritance, implementation, association, composition
}

public struct DiagramParameter: Codable, Sendable, Hashable {
    public let name: String
    public let type: String

    public init(name: String, type: String) {
        self.name = name
        self.type = type
    }
}

public struct DiagramTypeParameter: Codable, Sendable, Hashable {
    public let name: String
    public let constraints: String

    public init(name: String, constraints: String = "") {
        self.name = name
        self.constraints = constraints
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = try container.decode(String.self, forKey: .name)
        constraints = try container.decodeIfPresent(String.self, forKey: .constraints) ?? ""
    }
}

public struct DiagramMember: Codable, Sendable, Hashable {
    public let kind: MemberKind
    public let name: String
    public let type: String
    public let access: Access
    public let modifiers: MemberModifiers
    public let accessors: PropertyAccessors
    public let parameters: [DiagramParameter]
    public let value: String?

    public init(
        kind: MemberKind, name: String, type: String = "", access: Access = .public, modifiers: MemberModifiers = [],
        accessors: PropertyAccessors = .getSet, parameters: [DiagramParameter] = [], value: String? = nil
    ) {
        self.kind = kind
        self.name = name
        self.type = type
        self.access = access
        self.modifiers = modifiers
        self.accessors = accessors
        self.parameters = parameters
        self.value = value
    }

    enum CodingKeys: String, CodingKey {
        case kind, name, type, access, modifiers, accessors, parameters, value
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        kind = try c.decode(MemberKind.self, forKey: .kind)
        name = try c.decode(String.self, forKey: .name)
        type = try c.decodeIfPresent(String.self, forKey: .type) ?? ""
        access = try c.decodeIfPresent(Access.self, forKey: .access) ?? .public
        modifiers = try c.decodeIfPresent(MemberModifiers.self, forKey: .modifiers) ?? []
        accessors = try c.decodeIfPresent(PropertyAccessors.self, forKey: .accessors) ?? .getSet
        parameters = try c.decodeIfPresent([DiagramParameter].self, forKey: .parameters) ?? []
        value = try c.decodeIfPresent(String.self, forKey: .value)
    }

    /// Identifies the member within its type: the name, plus parameter types for methods and constructors.
    public var signature: String {
        kind == .method || kind == .constructor ? "\(name)(\(parameters.map(\.type).joined(separator: ",")))" : name
    }

    /// One UML-style line, e.g. `+ Total(withTax: bool): decimal`.
    public var umlLine: String {
        let parameterList = parameters.map { "\($0.name): \($0.type)" }.joined(separator: ", ")
        switch kind {
        case .enumValue: return value.map { "\(name) = \($0)" } ?? name
        case .method: return "\(access.symbol) \(name)(\(parameterList)): \(type)"
        case .constructor: return "\(access.symbol) \(name)(\(parameterList))"
        case .event: return "\(access.symbol) «event» \(name): \(type)"
        case .field, .property: return "\(access.symbol) \(name): \(type)"
        }
    }

    /// The text the member editor starts with (same notation the engine parses back).
    public var editText: String {
        var text = umlLine
        if kind != .enumValue, let value, !value.isEmpty {
            text += " = " + value
        }
        return text
    }
}

public struct DiagramType: Codable, Sendable, Hashable, Identifiable {
    public let fullName: String
    public let name: String
    public let namespace: String
    public let kind: DiagramTypeKind
    public let access: Access
    public let modifiers: TypeModifiers
    public let typeParameters: [DiagramTypeParameter]
    public let baseType: String?
    public let interfaces: [String]
    public let members: [DiagramMember]
    public let filePaths: [String]
    public let isExternal: Bool

    public var id: String { fullName }

    public init(
        fullName: String, name: String, namespace: String = "", kind: DiagramTypeKind, access: Access = .public,
        modifiers: TypeModifiers = [], typeParameters: [DiagramTypeParameter] = [], baseType: String? = nil,
        interfaces: [String] = [], members: [DiagramMember] = [], filePaths: [String] = [], isExternal: Bool = false
    ) {
        self.fullName = fullName
        self.name = name
        self.namespace = namespace
        self.kind = kind
        self.access = access
        self.modifiers = modifiers
        self.typeParameters = typeParameters
        self.baseType = baseType
        self.interfaces = interfaces
        self.members = members
        self.filePaths = filePaths
        self.isExternal = isExternal
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        fullName = try c.decode(String.self, forKey: .fullName)
        name = try c.decode(String.self, forKey: .name)
        namespace = try c.decodeIfPresent(String.self, forKey: .namespace) ?? ""
        kind = try c.decode(DiagramTypeKind.self, forKey: .kind)
        access = try c.decodeIfPresent(Access.self, forKey: .access) ?? .public
        modifiers = try c.decodeIfPresent(TypeModifiers.self, forKey: .modifiers) ?? []
        typeParameters = try c.decodeIfPresent([DiagramTypeParameter].self, forKey: .typeParameters) ?? []
        baseType = try c.decodeIfPresent(String.self, forKey: .baseType)
        interfaces = try c.decodeIfPresent([String].self, forKey: .interfaces) ?? []
        members = try c.decodeIfPresent([DiagramMember].self, forKey: .members) ?? []
        filePaths = try c.decodeIfPresent([String].self, forKey: .filePaths) ?? []
        isExternal = try c.decodeIfPresent(Bool.self, forKey: .isExternal) ?? false
    }

    /// Name with type parameters, e.g. `Repository<T>`.
    public var displayName: String {
        typeParameters.isEmpty ? name : "\(name)<\(typeParameters.map(\.name).joined(separator: ", "))>"
    }

    /// `«interface»`, `«abstract»` … or an empty string for a plain class.
    public var stereotype: String {
        switch kind {
        case .interface: return "«interface»"
        case .enum: return "«enum»"
        case .struct: return "«struct»"
        case .record: return "«record»"
        case .recordStruct: return "«record struct»"
        case .class:
            if modifiers.contains(.static) { return "«static»" }
            if modifiers.contains(.abstract) { return "«abstract»" }
            return isExternal ? "«extern»" : ""
        }
    }
}

public struct DiagramRelation: Codable, Sendable, Hashable {
    public let from: String
    public let to: String
    public let kind: RelationKind
    public let label: String?
    public let isMany: Bool

    public init(from: String, to: String, kind: RelationKind, label: String? = nil, isMany: Bool = false) {
        self.from = from
        self.to = to
        self.kind = kind
        self.label = label
        self.isMany = isMany
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        from = try c.decode(String.self, forKey: .from)
        to = try c.decode(String.self, forKey: .to)
        kind = try c.decode(RelationKind.self, forKey: .kind)
        label = try c.decodeIfPresent(String.self, forKey: .label)
        isMany = try c.decodeIfPresent(Bool.self, forKey: .isMany) ?? false
    }
}

public struct DiagramModel: Codable, Sendable, Hashable {
    public let types: [DiagramType]
    public let relations: [DiagramRelation]

    public init(types: [DiagramType], relations: [DiagramRelation]) {
        self.types = types
        self.relations = relations
    }

    public static let empty = DiagramModel(types: [], relations: [])

    public func find(_ fullName: String) -> DiagramType? { types.first { $0.fullName == fullName } }
}
