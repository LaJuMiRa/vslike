import Foundation

// Messages of the engine protocol (see engine/src/VsLike.Engine/Protocol). Offsets are UTF-16 code units, the same
// unit NSString and NSTextView use; lines and columns are 1-based.

public enum DiagnosticSeverity: String, Codable, Sendable, Comparable, CaseIterable {
    case hidden, info, warning, error

    private var rank: Int { Self.allCases.firstIndex(of: self)! }

    public static func < (lhs: Self, rhs: Self) -> Bool { lhs.rank < rhs.rank }
}

public struct Diagnostic: Codable, Sendable, Hashable, Identifiable {
    /// The diagnostic code (`CS0029`, `SA1028`).
    public let code: String
    public let message: String
    public let severity: DiagnosticSeverity
    public let filePath: String?
    public let start: Int
    public let length: Int
    public let startLine: Int
    public let startColumn: Int
    /// "Compiler", "StyleCop", "Analyzer" or "Build".
    public let category: String
    public let projectName: String?
    public let helpLink: String?

    public init(
        code: String, message: String, severity: DiagnosticSeverity, filePath: String?, start: Int = 0, length: Int = 0,
        startLine: Int, startColumn: Int, category: String, projectName: String? = nil, helpLink: String? = nil
    ) {
        self.code = code
        self.message = message
        self.severity = severity
        self.filePath = filePath
        self.start = start
        self.length = length
        self.startLine = startLine
        self.startColumn = startColumn
        self.category = category
        self.projectName = projectName
        self.helpLink = helpLink
    }

    enum CodingKeys: String, CodingKey {
        case code = "id"
        case message, severity, filePath, start, length, startLine, startColumn, category, projectName, helpLink
    }

    public var id: String { "\(filePath ?? ""):\(startLine):\(startColumn):\(code):\(message)" }

    public var isStyleCop: Bool { category == "StyleCop" }
}

public enum CompletionKind: String, Codable, Sendable {
    case other, keyword, `class`, `struct`, interface, `enum`, enumMember, delegate, method, extensionMethod, property, field,
        event, local, parameter, namespace, typeParameter, constant, snippet

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = CompletionKind(rawValue: raw) ?? .other
    }
}

public struct CompletionItem: Codable, Sendable, Hashable, Identifiable {
    public let displayText: String
    public let filterText: String
    public let sortText: String
    public let kind: CompletionKind
    public let inlineDescription: String?
    public let isPreselected: Bool
    public let handle: Int

    public var id: Int { handle }

    public init(displayText: String, filterText: String? = nil, sortText: String? = nil, kind: CompletionKind, inlineDescription: String? = nil, isPreselected: Bool = false, handle: Int) {
        self.displayText = displayText
        self.filterText = filterText ?? displayText
        self.sortText = sortText ?? displayText
        self.kind = kind
        self.inlineDescription = inlineDescription
        self.isPreselected = isPreselected
        self.handle = handle
    }
}

public struct CompletionList: Codable, Sendable {
    public let items: [CompletionItem]
    public let spanStart: Int
    public let spanLength: Int
}

public struct TextEdit: Codable, Sendable, Hashable {
    public let start: Int
    public let length: Int
    public let newText: String

    public init(start: Int, length: Int, newText: String) {
        self.start = start
        self.length = length
        self.newText = newText
    }
}

public struct FileEdit: Codable, Sendable, Hashable {
    public let filePath: String
    public let edits: [TextEdit]
    /// Complete content when the edit creates the file.
    public let newFileText: String?

    public init(filePath: String, edits: [TextEdit], newFileText: String? = nil) {
        self.filePath = filePath
        self.edits = edits
        self.newFileText = newFileText
    }
}

public struct WorkspaceEdit: Codable, Sendable, Hashable {
    public let files: [FileEdit]
    public let caretFile: String?
    public let caretOffset: Int?

    public init(files: [FileEdit], caretFile: String? = nil, caretOffset: Int? = nil) {
        self.files = files
        self.caretFile = caretFile
        self.caretOffset = caretOffset
    }

    public static let empty = WorkspaceEdit(files: [])

    public var isEmpty: Bool { files.allSatisfy { $0.edits.isEmpty && $0.newFileText == nil } }
}

public struct CodeAction: Codable, Sendable, Hashable, Identifiable {
    public let title: String
    /// Diagnostic id for fixes ("SA1028", "CS0103"), otherwise a refactoring group.
    public let group: String
    public let isFix: Bool
    public let handle: Int

    public var id: Int { handle }

    public init(title: String, group: String, isFix: Bool, handle: Int) {
        self.title = title
        self.group = group
        self.isFix = isFix
        self.handle = handle
    }

    public var isFixAll: Bool { title.hasPrefix("Fix all") || title.hasPrefix("Alle im") }
}

public struct QuickInfo: Codable, Sendable {
    public let start: Int
    public let length: Int
    public let signature: String
    public let documentation: String?
    public let diagnostics: [Diagnostic]

    public init(start: Int, length: Int, signature: String, documentation: String?, diagnostics: [Diagnostic]) {
        self.start = start
        self.length = length
        self.signature = signature
        self.documentation = documentation
        self.diagnostics = diagnostics
    }
}

public struct SignatureInfo: Codable, Sendable, Hashable {
    public let prefix: String
    public let parameters: [String]
    public let suffix: String
    public let documentation: String?

    public var label: String { prefix + parameters.joined(separator: ", ") + suffix }
}

public struct SignatureHelp: Codable, Sendable {
    public let signatures: [SignatureInfo]
    public let activeSignature: Int
    public let activeParameter: Int
}

public struct SourceLocation: Codable, Sendable, Hashable, Identifiable {
    public let filePath: String
    public let start: Int
    public let length: Int
    public let line: Int
    public let column: Int
    public let lineText: String

    public var id: String { "\(filePath):\(start)" }
}

public struct RenameTarget: Codable, Sendable {
    public let currentName: String
    public let symbolDisplay: String
    public let canRename: Bool
    public let reason: String?
    public let referenceCount: Int
}

public struct ClassifiedSpan: Codable, Sendable, Hashable {
    public let start: Int
    public let length: Int
    public let classification: String
}

public struct WorkspaceProject: Codable, Sendable, Hashable, Identifiable {
    public let name: String
    public let filePath: String
    public let targetFramework: String?
    public let rootNamespace: String?
    public let projectReferences: [String]
    public let documents: [String]

    public var id: String { filePath }
}

public struct WorkspaceLoadResult: Codable, Sendable {
    public let path: String
    public let projects: [WorkspaceProject]
    public let warnings: [String]
    /// .NET TimeSpan text, e.g. "00:00:03.1234567".
    public let duration: String
}

public struct EngineInfo: Codable, Sendable {
    public let engineVersion: String
    public let msBuildPath: String
    public let sdkVersion: String

    enum CodingKeys: String, CodingKey {
        case engineVersion
        case msBuildPath = "msBuildPath"
        case sdkVersion
    }
}
