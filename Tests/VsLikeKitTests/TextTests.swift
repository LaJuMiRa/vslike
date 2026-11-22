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

    @Test func namespace_fileScopedBecomesBlockScopedWithIndentedBody() {
        let source = "\u{FEFF}namespace test;\n\nclass Program\n{\n    static void Main(string[] args)\n    {\n        Console.WriteLine(\"Hello, World!\");\n    }\n}\n"

        let converted = NamespaceStyle.blockScoped(source)

        #expect(converted == "\u{FEFF}namespace test\n{\n    class Program\n    {\n        static void Main(string[] args)\n        {\n            Console.WriteLine(\"Hello, World!\");\n        }\n    }\n}\n")
        #expect(NamespaceStyle.blockScoped(converted) == converted)
        #expect(NamespaceStyle.blockScoped("using System;\r\nnamespace A.B;\r\npublic class C { }\r\n") == "using System;\r\nnamespace A.B\r\n{\r\n    public class C { }\r\n}\r\n")
    }

    @Test func styleCopSettings_containCompanyAndCopyright() throws {
        let text = try StyleCopSettingsFile.contents(companyName: "Acme \"GmbH\"", copyrightText: "")
        let json = try #require(try JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
        let rules = try #require((json["settings"] as? [String: Any])?["documentationRules"] as? [String: String])

        #expect(rules["companyName"] == "Acme \"GmbH\"")
        #expect(rules["copyrightText"] == StyleCopSettingsFile.defaultCopyrightText)
        #expect(StyleCopSettingsFile.url(forSolution: URL(fileURLWithPath: "/x/App.slnx")).path == "/x/stylecop.json")
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

    @Test func newline_afterTypedEmptyBracesOpensTheBlock() {
        let text = "public void test()\n{}\n" as NSString
        let location = TextEditing.newlineLocation(for: 21, in: text)
        let result = TextEditing.newline(at: location, in: text)

        #expect(location == 20)
        #expect(TextEditing.apply([TextEdit(start: location, length: 0, newText: result.text)], to: text as String) == "public void test()\n{\n    \n}\n")
        #expect(location + result.caret == 25)
        // Not after `{}` followed by more code, and not elsewhere.
        #expect(TextEditing.newlineLocation(for: 6, in: "x = {} + y" as NSString) == 6)
        #expect(TextEditing.newlineLocation(for: 3, in: "a()" as NSString) == 3)
    }

    @Test func newlineEdit_movesBraceAfterCodeOntoItsOwnLine() {
        let text = "    public void test() {}\n" as NSString
        let result = TextEditing.newlineEdit(at: 24, in: text)

        #expect(TextEditing.apply([result.edit], to: text as String) == "    public void test()\n    {\n        \n    }\n")
        #expect(result.caret == 37)
    }

    @Test func newlineEdit_afterTypedBracesAfterCodeOpensTheBlock() {
        let text = "void Run() {}" as NSString
        let result = TextEditing.newlineEdit(at: TextEditing.newlineLocation(for: 13, in: text), in: text)

        #expect(TextEditing.apply([result.edit], to: text as String) == "void Run()\n{\n    \n}")
        #expect(result.caret == 17)
    }

    @Test func newlineEdit_leavesBraceAloneOnItsLineAndInComments() {
        let alone = "void Run()\n{}" as NSString
        #expect(TextEditing.apply([TextEditing.newlineEdit(at: 12, in: alone).edit], to: alone as String) == "void Run()\n{\n    \n}")
        let comment = "// see {" as NSString
        #expect(TextEditing.newlineEdit(at: 8, in: comment).edit == TextEdit(start: 8, length: 0, newText: "\n    "))
    }

    @Test func completionEdits_replaceEverythingTypedSinceTheListOpened() {
        // List opened after a space (empty span), then `vo` was typed: the insertion must replace `vo`.
        let text = "static vo"
        let empty = TextEditing.completionEdits([TextEdit(start: 7, length: 0, newText: "void")], spanStart: 7, caret: 9)
        #expect(TextEditing.apply(empty, to: text) == "static void")
        // List opened on `v`.
        let one = TextEditing.completionEdits([TextEdit(start: 7, length: 1, newText: "void")], spanStart: 7, caret: 9)
        #expect(TextEditing.apply(one, to: text) == "static void")
        // Other edits (an added using) stay as they are.
        let using = TextEdit(start: 0, length: 0, newText: "using System.Text;\n")
        #expect(TextEditing.completionEdits([using, TextEdit(start: 7, length: 2, newText: "void")], spanStart: 7, caret: 9).first == using)
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
