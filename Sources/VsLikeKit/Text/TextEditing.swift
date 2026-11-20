import Foundation

/// Text operations of the editor that do not need AppKit: applying edits, indentation, comment toggling and
/// bracket matching. All offsets are UTF-16.
public enum TextEditing {
    public static let indentUnit = "    "

    /// Applies non-overlapping edits (any order) to a text.
    public static func apply(_ edits: [TextEdit], to text: String) -> String {
        let result = NSMutableString(string: text)
        for edit in edits.sorted(by: { $0.start > $1.start }) {
            let start = min(edit.start, result.length)
            result.replaceCharacters(in: NSRange(location: start, length: min(edit.length, result.length - start)), with: edit.newText)
        }
        return result as String
    }

    /// The smallest single edit that turns `before` into `after` (common prefix and suffix removed).
    public static func minimalEdit(from before: String, to after: String) -> TextEdit {
        let a = Array(before.utf16)
        let b = Array(after.utf16)
        var prefix = 0
        while prefix < a.count && prefix < b.count && a[prefix] == b[prefix] {
            prefix += 1
        }
        var suffix = 0
        while suffix < a.count - prefix && suffix < b.count - prefix && a[a.count - 1 - suffix] == b[b.count - 1 - suffix] {
            suffix += 1
        }
        let inserted = String(utf16CodeUnits: Array(b[prefix..<(b.count - suffix)]), count: b.count - suffix - prefix)
        return TextEdit(start: prefix, length: a.count - prefix - suffix, newText: inserted)
    }

    /// Leading whitespace of the line that contains `offset`.
    public static func indentation(ofLineAt offset: Int, in text: NSString) -> String {
        let line = text.lineRange(for: NSRange(location: min(offset, text.length), length: 0))
        var end = line.location
        while end < NSMaxRange(line), let scalar = UnicodeScalar(text.character(at: end)), scalar == " " || scalar == "\t" {
            end += 1
        }
        return text.substring(with: NSRange(location: line.location, length: end - line.location))
    }

    /// Text to insert for Return at `offset`: newline plus indentation, one level deeper after `{`, and the closing
    /// brace on its own line when the caret sits between `{` and `}`. Returns the text and the caret offset in it.
    public static func newline(at offset: Int, in text: NSString) -> (text: String, caret: Int) {
        let indent = indentation(ofLineAt: offset, in: text)
        let lineStart = text.lineRange(for: NSRange(location: min(offset, text.length), length: 0)).location
        let before = text.substring(with: NSRange(location: lineStart, length: offset - lineStart)).trimmingCharacters(in: .whitespaces)
        let next = offset < text.length ? text.character(at: offset) : 0
        if before.hasSuffix("{") || before.hasSuffix("[") || before.hasSuffix("(") {
            let inner = "\n" + indent + indentUnit
            let closesHere = next == UInt16(UnicodeScalar("}").value) || next == UInt16(UnicodeScalar("]").value) || next == UInt16(UnicodeScalar(")").value)
            if closesHere {
                return (inner + "\n" + indent, inner.utf16.count)
            }
            return (inner, inner.utf16.count)
        }
        let inserted = "\n" + indent
        return (inserted, inserted.utf16.count)
    }

    /// The closing character auto-inserted after typing `character`, if any.
    public static func closingPair(for character: Character) -> Character? {
        switch character {
        case "(": return ")"
        case "[": return "]"
        case "{": return "}"
        case "\"": return "\""
        case "'": return "'"
        default: return nil
        }
    }

    /// Toggles `//` on all lines touched by `range`. Returns the replaced range of whole lines and the new text.
    public static func toggleLineComment(in text: NSString, range: NSRange) -> (range: NSRange, text: String) {
        let lines = text.lineRange(for: range)
        let block = text.substring(with: lines)
        var parts = block.components(separatedBy: "\n")
        let trailingNewline = parts.last == ""
        if trailingNewline {
            parts.removeLast()
        }
        let nonBlank = parts.filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
        let allCommented = !nonBlank.isEmpty && nonBlank.allSatisfy { $0.trimmingCharacters(in: .whitespaces).hasPrefix("//") }
        let minIndent = nonBlank.map { $0.prefix { $0 == " " || $0 == "\t" }.count }.min() ?? 0
        let changed = parts.map { line -> String in
            if line.trimmingCharacters(in: .whitespaces).isEmpty {
                return line
            }
            if allCommented {
                guard let slashes = line.range(of: "//") else { return line }
                var end = slashes.upperBound
                if end < line.endIndex, line[end] == " " {
                    end = line.index(after: end)
                }
                return String(line[..<slashes.lowerBound]) + String(line[end...])
            }
            let index = line.index(line.startIndex, offsetBy: minIndent)
            return String(line[..<index]) + "// " + String(line[index...])
        }
        return (lines, changed.joined(separator: "\n") + (trailingNewline ? "\n" : ""))
    }

    /// Offset of the bracket matching the one at `offset` (ignores brackets in strings and comments via the tokens).
    public static func matchingBracket(at offset: Int, in units: [UInt16], tokens: [Token]) -> Int? {
        guard offset >= 0, offset < units.count else {
            return nil
        }
        let pairs: [UInt16: (UInt16, Int)] = [40: (41, 1), 91: (93, 1), 123: (125, 1), 41: (40, -1), 93: (91, -1), 125: (123, -1)]
        guard let (target, direction) = pairs[units[offset]] else {
            return nil
        }
        let skip = tokens.filter { [.string, .character, .comment, .docComment].contains($0.kind) }
        func isSkipped(_ index: Int) -> Bool {
            skip.contains { index >= $0.start && index < $0.start + $0.length }
        }
        if isSkipped(offset) {
            return nil
        }
        let open = units[offset]
        var depth = 0
        var index = offset
        while index >= 0 && index < units.count {
            let unit = units[index]
            if (unit == open || unit == target) && !isSkipped(index) {
                depth += unit == open ? 1 : -1
                if depth == 0 {
                    return index
                }
            }
            index += direction
        }
        return nil
    }
}

/// Subsequence matching for the command palette and quick open: matches at word starts and consecutive matches score
/// higher. Returns nil when `query` is not a subsequence of `candidate`.
public enum FuzzyMatcher {
    public static func score(_ query: String, _ candidate: String) -> Int? {
        let needle = Array(query.lowercased().filter { !$0.isWhitespace })
        guard !needle.isEmpty else {
            return 0
        }
        let haystack = Array(candidate)
        let lower = Array(candidate.lowercased())
        guard lower.count == haystack.count else {
            return candidate.lowercased().contains(String(needle)) ? 1 : nil
        }
        var score = 0
        var position = 0
        var previous = -2
        for character in needle {
            guard let found = (position..<lower.count).first(where: { lower[$0] == character }) else {
                return nil
            }
            score += 1
            if found == previous + 1 {
                score += 5
            }
            if found == 0 || !haystack[found - 1].isLetter && !haystack[found - 1].isNumber || haystack[found].isUppercase && haystack[found - 1].isLowercase {
                score += 8
            }
            previous = found
            position = found + 1
        }
        return score - haystack.count / 10
    }
}
