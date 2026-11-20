import Foundation

public enum TokenKind: Sendable, Hashable {
    case keyword, controlKeyword, typeKeyword, identifier, number, string, character, comment, docComment, preprocessor, `operator`, punctuation
}

public struct Token: Sendable, Hashable {
    public let kind: TokenKind
    /// UTF-16 offset and length (the units of NSString and NSTextView).
    public let start: Int
    public let length: Int
}

/// A fast lexical tokenizer for C# used for immediate highlighting while typing. Roslyn's semantic classification
/// (types, methods, locals …) is layered on top when it arrives. Works on UTF-16 code units.
public enum CSharpLexer {
    // Contextual keywords that are common identifiers (value, args, from, select …) are left to Roslyn's semantic
    // classification, which colours them only where they act as keywords.
    static let keywords: Set<String> = [
        "abstract", "as", "base", "checked", "class", "const", "default", "delegate", "enum", "event", "explicit", "extern",
        "false", "fixed", "implicit", "in", "interface", "internal", "is", "lock", "namespace", "new", "null", "operator",
        "out", "override", "params", "private", "protected", "public", "readonly", "ref", "sealed", "sizeof", "stackalloc",
        "static", "struct", "this", "true", "typeof", "unchecked", "unsafe", "using", "virtual", "volatile", "record",
        "get", "set", "init", "var", "async", "await", "partial", "where", "required", "nameof", "global", "with", "when",
        "and", "or", "not", "dynamic",
    ]
    static let controlKeywords: Set<String> = [
        "if", "else", "switch", "case", "do", "for", "foreach", "while", "break", "continue", "return", "goto", "throw",
        "try", "catch", "finally", "yield",
    ]
    static let typeKeywords: Set<String> = [
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint", "long", "ulong", "short", "ushort",
        "object", "string", "void", "nint", "nuint",
    ]

    public static func tokenize(_ text: String) -> [Token] {
        tokenize(Array(text.utf16))
    }

    public static func tokenize(_ s: [UInt16]) -> [Token] {
        var tokens: [Token] = []
        tokens.reserveCapacity(s.count / 4)
        var i = 0
        let n = s.count
        var lineStart = true

        func at(_ index: Int) -> UInt16 { index < n ? s[index] : 0 }
        func add(_ kind: TokenKind, _ start: Int, _ end: Int) {
            if end > start { tokens.append(Token(kind: kind, start: start, length: end - start)) }
        }

        while i < n {
            let c = s[i]
            if c == 10 {
                lineStart = true
                i += 1
                continue
            }
            if c == 32 || c == 9 || c == 13 {
                i += 1
                continue
            }
            let start = i
            defer { lineStart = false }

            // Preprocessor directive: # as first non-blank character of a line.
            if c == 35 && lineStart {
                while i < n && s[i] != 10 { i += 1 }
                add(.preprocessor, start, i)
                continue
            }
            // Comments.
            if c == 47 && at(i + 1) == 47 {
                let isDoc = at(i + 2) == 47 && at(i + 3) != 47
                while i < n && s[i] != 10 { i += 1 }
                add(isDoc ? .docComment : .comment, start, i)
                continue
            }
            if c == 47 && at(i + 1) == 42 {
                i += 2
                while i < n && !(s[i] == 42 && at(i + 1) == 47) { i += 1 }
                i = min(n, i + 2)
                add(.comment, start, i)
                continue
            }
            // Strings: prefixes $ @ (any order), raw """…""".
            if c == 34 || ((c == 36 || c == 64) && (at(i + 1) == 34 || ((at(i + 1) == 36 || at(i + 1) == 64) && at(i + 2) == 34))) {
                var verbatim = false
                var dollars = 0
                while s[i] == 36 || s[i] == 64 {
                    if s[i] == 64 { verbatim = true } else { dollars += 1 }
                    i += 1
                }
                var quotes = 0
                while at(i + quotes) == 34 { quotes += 1 }
                if quotes >= 3 {
                    i += quotes
                    while i < n {
                        if s[i] == 34 {
                            var run = 0
                            while at(i + run) == 34 { run += 1 }
                            i += run
                            if run >= quotes { break }
                        } else {
                            i += 1
                        }
                    }
                } else {
                    i += 1
                    var braceDepth = 0
                    while i < n {
                        let ch = s[i]
                        if verbatim {
                            if ch == 34 {
                                if at(i + 1) == 34 { i += 2; continue }
                                i += 1
                                break
                            }
                        } else {
                            if ch == 92 { i += 2; continue }
                            if ch == 10 { break }
                            if ch == 34 && braceDepth == 0 { i += 1; break }
                        }
                        if dollars > 0 {
                            if ch == 123 && at(i + 1) != 123 { braceDepth += 1 } else if ch == 125 && braceDepth > 0 { braceDepth -= 1 }
                        }
                        i += 1
                    }
                }
                add(.string, start, min(i, n))
                continue
            }
            // Character literal.
            if c == 39 {
                i += 1
                while i < n && s[i] != 39 && s[i] != 10 {
                    i += s[i] == 92 ? 2 : 1
                }
                i = min(n, i + 1)
                add(.character, start, i)
                continue
            }
            // Numbers.
            if isDigit(c) || (c == 46 && isDigit(at(i + 1))) {
                i += 1
                while i < n && (isIdentifierPart(s[i]) || s[i] == 46 && isDigit(at(i + 1))) { i += 1 }
                add(.number, start, i)
                continue
            }
            // Identifiers and keywords (@ escapes a keyword).
            if isIdentifierStart(c) || (c == 64 && isIdentifierStart(at(i + 1))) {
                let escaped = c == 64
                i += 1
                while i < n && isIdentifierPart(s[i]) { i += 1 }
                if escaped {
                    add(.identifier, start, i)
                    continue
                }
                let word = String(utf16CodeUnits: Array(s[start..<i]), count: i - start)
                let kind: TokenKind
                if controlKeywords.contains(word) {
                    kind = .controlKeyword
                } else if typeKeywords.contains(word) {
                    kind = .typeKeyword
                } else if keywords.contains(word) {
                    kind = .keyword
                } else {
                    kind = .identifier
                }
                add(kind, start, i)
                continue
            }
            if "{}()[];,.".utf16.contains(c) {
                add(.punctuation, start, i + 1)
            } else {
                add(.operator, start, i + 1)
            }
            i += 1
        }
        return tokens
    }

    public static func isDigit(_ c: UInt16) -> Bool { c >= 48 && c <= 57 }

    public static func isIdentifierStart(_ c: UInt16) -> Bool {
        (c >= 65 && c <= 90) || (c >= 97 && c <= 122) || c == 95 || c > 127
    }

    public static func isIdentifierPart(_ c: UInt16) -> Bool { isIdentifierStart(c) || isDigit(c) }
}
