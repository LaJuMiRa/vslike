import Foundation
import Testing
@testable import VsLikeKit

@Suite("Lexer, snippets, text editing and fuzzy matching")
struct TextTests {
    func kinds(_ source: String) -> [(TokenKind, String)] {
        let units = Array(source.utf16)
        return CSharpLexer.tokenize(source).map { token in
            (token.kind, String(utf16CodeUnits: Array(units[token.start..<(token.start + token.length)]), count: token.length))
        }
    }

    @Test func lexer_classifiesKeywordsTypesLiteralsAndComments() {
        let tokens = kinds("public int Count = 42; // total\nif (x) return \"a\\\"b\";")

        #expect(tokens.contains { $0 == (.keyword, "public") })
        #expect(tokens.contains { $0 == (.typeKeyword, "int") })
        #expect(tokens.contains { $0 == (.identifier, "Count") })
        #expect(tokens.contains { $0 == (.number, "42") })
        #expect(tokens.contains { $0 == (.comment, "// total") })
        #expect(tokens.contains { $0 == (.controlKeyword, "if") })
        #expect(tokens.contains { $0 == (.string, "\"a\\\"b\"") })
    }

    @Test func lexer_handlesVerbatimInterpolatedRawStringsAndPreprocessor() {
        let source = "#region Demo\nvar a = @\"c:\\x\"\"y\"; var b = $\"{x + \"q\"} done\"; var c = \"\"\"\nraw \"quoted\"\n\"\"\";\n/* block\n comment */ ///doc"
        let tokens = kinds(source)

        #expect(tokens.first! == (.preprocessor, "#region Demo"))
        #expect(tokens.contains { $0 == (.string, "@\"c:\\x\"\"y\"") })
        #expect(tokens.contains { $0 == (.string, "$\"{x + \"q\"} done\"") })
        #expect(tokens.contains { $0 == (.string, "\"\"\"\nraw \"quoted\"\n\"\"\"") })
        #expect(tokens.contains { $0 == (.comment, "/* block\n comment */") })
        #expect(tokens.last! == (.docComment, "///doc"))
    }

    @Test func lexer_usesUTF16Offsets() {
        let source = "var 😀 = \"ä\"; int x;"
        let token = CSharpLexer.tokenize(source).first { $0.kind == .typeKeyword }!

        #expect((source as NSString).substring(with: NSRange(location: token.start, length: token.length)) == "int")
    }

    @Test func snippet_expandsPlaceholdersIndentationAndType() {
        let expanded = SnippetExpander.expand(Snippet.find("ctor")!.body, indentation: "    ", typeName: "Car")

        #expect(expanded.text == "public Car()\n    {\n        \n    }")
        #expect(expanded.placeholders.map(\.index) == [1])
        #expect(expanded.finalCaret == ("public Car()\n    {\n        " as NSString).length)

        let prop = SnippetExpander.expand(Snippet.find("propfull")!.body, indentation: "", typeName: nil)
        #expect(prop.stops == [1, 2, 3])
        #expect(prop.placeholders.filter { $0.index == 2 }.count == 3)
    }

    @Test func applyAndMinimalEdit() {
        let text = "Hello brave new world"
        let edited = TextEditing.apply([TextEdit(start: 0, length: 5, newText: "Hi"), TextEdit(start: 12, length: 3, newText: "old")], to: text)

        #expect(edited == "Hi brave old world")
        let minimal = TextEditing.minimalEdit(from: text, to: edited)
        #expect(TextEditing.apply([minimal], to: text) == edited)
        #expect(minimal.start == 1)
    }

    @Test func newline_indentsAfterBraceAndSplitsBracePair() {
        let text = "class A\n    {}" as NSString
        let result = TextEditing.newline(at: 13, in: text)

        #expect(result.text == "\n        \n    ")
        #expect(result.caret == 9)
        #expect(TextEditing.newline(at: 7, in: text).text == "\n")
    }

    @Test func toggleComment_commentsAndUncommentsLinesAtCommonIndent() {
        let text = "    a();\n      b();\n" as NSString
        let commented = TextEditing.toggleLineComment(in: text, range: NSRange(location: 0, length: text.length))
        #expect(commented.text == "    // a();\n    //   b();\n")

        let back = TextEditing.toggleLineComment(in: commented.text as NSString, range: NSRange(location: 0, length: 5))
        #expect(back.text == "    a();\n")
    }

    @Test func matchingBracket_skipsStrings() {
        let source = "f(\"(\", g(1))"
        let units = Array(source.utf16)

        #expect(TextEditing.matchingBracket(at: 1, in: units, tokens: CSharpLexer.tokenize(units)) == 11)
        #expect(TextEditing.matchingBracket(at: 11, in: units, tokens: CSharpLexer.tokenize(units)) == 1)
    }

    @Test func fuzzy_prefersWordStartsAndRejectsNonSubsequences() {
        let build = FuzzyMatcher.score("bs", "Build Solution")!
        let other = FuzzyMatcher.score("bs", "Absolute")!

        #expect(build > other)
        #expect(FuzzyMatcher.score("xyz", "Build") == nil)
        #expect(FuzzyMatcher.score("", "Build") == 0)
    }

    @Test func framer_splitsAndJoinsChunks() {
        var framer = MessageFramer()
        let one = MessageFramer.frame(Data("{\"a\":1}".utf8))
        let two = MessageFramer.frame(Data("{\"b\":2}".utf8))
        let stream = one + two

        let first = framer.append(stream.prefix(10))
        let rest = framer.append(stream.dropFirst(10))

        #expect(first.isEmpty)
        #expect(rest.map { String(decoding: $0, as: UTF8.self) } == ["{\"a\":1}", "{\"b\":2}"])
    }
}
