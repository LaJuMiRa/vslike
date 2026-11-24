using System.Text;
using System.Text.Json;
using VsLike.Kit;
using Xunit;

namespace VsLike.Kit.Tests;

public sealed class TextTests
{
    [Fact]
    public void Namespace_FileScopedBecomesBlockScopedWithIndentedBody()
    {
        const string source = "\uFEFFnamespace test;\n\nclass Program\n{\n    static void Main(string[] args)\n    {\n        Console.WriteLine(\"Hello, World!\");\n    }\n}\n";

        var converted = NamespaceStyle.BlockScoped(source);

        Assert.Equal("\uFEFFnamespace test\n{\n    class Program\n    {\n        static void Main(string[] args)\n        {\n            Console.WriteLine(\"Hello, World!\");\n        }\n    }\n}\n", converted);
        Assert.Equal(converted, NamespaceStyle.BlockScoped(converted));
        Assert.Equal("using System;\r\nnamespace A.B\r\n{\r\n    public class C { }\r\n}\r\n", NamespaceStyle.BlockScoped("using System;\r\nnamespace A.B;\r\npublic class C { }\r\n"));
    }

    [Fact]
    public void StyleCopSettings_ContainCompanyAndCopyright()
    {
        var text = StyleCopSettingsFile.Contents("Acme \"GmbH\"", string.Empty);
        using var json = JsonDocument.Parse(text);
        var rules = json.RootElement.GetProperty("settings").GetProperty("documentationRules");

        Assert.Equal("Acme \"GmbH\"", rules.GetProperty("companyName").GetString());
        Assert.Equal(StyleCopSettingsFile.DefaultCopyrightText, rules.GetProperty("copyrightText").GetString());
        Assert.Equal("/x/stylecop.json", StyleCopSettingsFile.Path("/x/App.slnx"));
    }

    [Fact]
    public void Lexer_ClassifiesKeywordsTypesLiteralsAndComments()
    {
        var tokens = Kinds("public int Count = 42; // total\nif (x) return \"a\\\"b\";");

        Assert.Contains((TokenKind.Keyword, "public"), tokens);
        Assert.Contains((TokenKind.TypeKeyword, "int"), tokens);
        Assert.Contains((TokenKind.Identifier, "Count"), tokens);
        Assert.Contains((TokenKind.Number, "42"), tokens);
        Assert.Contains((TokenKind.Comment, "// total"), tokens);
        Assert.Contains((TokenKind.ControlKeyword, "if"), tokens);
        Assert.Contains((TokenKind.String, "\"a\\\"b\""), tokens);
    }

    [Fact]
    public void Lexer_HandlesVerbatimInterpolatedRawStringsAndPreprocessor()
    {
        const string source = "#region Demo\nvar a = @\"c:\\x\"\"y\"; var b = $\"{x + \"q\"} done\"; var c = \"\"\"\nraw \"quoted\"\n\"\"\";\n/* block\n comment */ ///doc";
        var tokens = Kinds(source);

        Assert.Equal((TokenKind.Preprocessor, "#region Demo"), tokens[0]);
        Assert.Contains((TokenKind.String, "@\"c:\\x\"\"y\""), tokens);
        Assert.Contains((TokenKind.String, "$\"{x + \"q\"} done\""), tokens);
        Assert.Contains((TokenKind.String, "\"\"\"\nraw \"quoted\"\n\"\"\""), tokens);
        Assert.Contains((TokenKind.Comment, "/* block\n comment */"), tokens);
        Assert.Equal((TokenKind.DocComment, "///doc"), tokens[^1]);
    }

    [Fact]
    public void Lexer_UsesUtf16Offsets()
    {
        const string source = "var 😀 = \"ä\"; int x;";
        var token = CSharpLexer.Tokenize(source).First(t => t.Kind == TokenKind.TypeKeyword);

        Assert.Equal("int", source.Substring(token.Start, token.Length));
    }

    [Fact]
    public void Snippet_ExpandsPlaceholdersIndentationAndType()
    {
        var expanded = SnippetExpander.Expand(Snippet.Find("ctor")!.Body, "    ", "Car");

        Assert.Equal("public Car()\n    {\n        \n    }", expanded.Text);
        Assert.Equal([1], expanded.Placeholders.Select(p => p.Index));
        Assert.Equal("public Car()\n    {\n        ".Length, expanded.FinalCaret);

        var prop = SnippetExpander.Expand(Snippet.Find("propfull")!.Body, string.Empty, null);
        Assert.Equal([1, 2, 3], prop.Stops);
        Assert.Equal(3, prop.Placeholders.Count(p => p.Index == 2));
    }

    [Fact]
    public void ApplyAndMinimalEdit()
    {
        const string text = "Hello brave new world";
        var edited = TextEditing.Apply([new TextEdit(0, 5, "Hi"), new TextEdit(12, 3, "old")], text);

        Assert.Equal("Hi brave old world", edited);
        var minimal = TextEditing.MinimalEdit(text, edited);
        Assert.Equal(edited, TextEditing.Apply([minimal], text));
        Assert.Equal(1, minimal.Start);
    }

    [Fact]
    public void Newline_IndentsAfterBraceAndSplitsBracePair()
    {
        const string text = "class A\n    {}";
        var result = TextEditing.Newline(13, text);

        Assert.Equal("\n        \n    ", result.Text);
        Assert.Equal(9, result.Caret);
        Assert.Equal("\n", TextEditing.Newline(7, text).Text);
    }

    [Fact]
    public void Newline_AfterTypedEmptyBracesOpensTheBlock()
    {
        const string text = "public void test()\n{}\n";
        var location = TextEditing.NewlineLocation(21, text);
        var result = TextEditing.Newline(location, text);

        Assert.Equal(20, location);
        Assert.Equal("public void test()\n{\n    \n}\n", TextEditing.Apply([new TextEdit(location, 0, result.Text)], text));
        Assert.Equal(25, location + result.Caret);
        Assert.Equal(6, TextEditing.NewlineLocation(6, "x = {} + y"));
        Assert.Equal(3, TextEditing.NewlineLocation(3, "a()"));
    }

    [Fact]
    public void NewlineEdit_MovesBraceAfterCodeOntoItsOwnLine()
    {
        const string text = "    public void test() {}\n";
        var result = TextEditing.NewlineEdit(24, text);

        Assert.Equal("    public void test()\n    {\n        \n    }\n", TextEditing.Apply([result.Edit], text));
        Assert.Equal(37, result.Caret);
    }

    [Fact]
    public void NewlineEdit_AfterTypedBracesAfterCodeOpensTheBlock()
    {
        const string text = "void Run() {}";
        var result = TextEditing.NewlineEdit(TextEditing.NewlineLocation(13, text), text);

        Assert.Equal("void Run()\n{\n    \n}", TextEditing.Apply([result.Edit], text));
        Assert.Equal(17, result.Caret);
    }

    [Fact]
    public void NewlineEdit_LeavesBraceAloneOnItsLineAndInComments()
    {
        const string alone = "void Run()\n{}";
        Assert.Equal("void Run()\n{\n    \n}", TextEditing.Apply([TextEditing.NewlineEdit(12, alone).Edit], alone));
        Assert.Equal(new TextEdit(8, 0, "\n    "), TextEditing.NewlineEdit(8, "// see {").Edit);
    }

    [Fact]
    public void CompletionEdits_ReplaceEverythingTypedSinceTheListOpened()
    {
        const string text = "static vo";
        Assert.Equal("static void", TextEditing.Apply(TextEditing.CompletionEdits([new TextEdit(7, 0, "void")], 7, 9), text));
        Assert.Equal("static void", TextEditing.Apply(TextEditing.CompletionEdits([new TextEdit(7, 1, "void")], 7, 9), text));
        var usingEdit = new TextEdit(0, 0, "using System.Text;\n");
        Assert.Equal(usingEdit, TextEditing.CompletionEdits([usingEdit, new TextEdit(7, 2, "void")], 7, 9)[0]);
    }

    [Fact]
    public void ToggleComment_CommentsAndUncommentsLinesAtCommonIndent()
    {
        const string text = "    a();\n      b();\n";
        var commented = TextEditing.ToggleLineComment(text, new TextRange(0, text.Length));
        Assert.Equal("    // a();\n    //   b();\n", commented.Text);

        var back = TextEditing.ToggleLineComment(commented.Text, new TextRange(0, 5));
        Assert.Equal("    a();\n", back.Text);
    }

    [Fact]
    public void MatchingBracket_SkipsStrings()
    {
        const string source = "f(\"(\", g(1))";

        Assert.Equal(11, TextEditing.MatchingBracket(1, source, CSharpLexer.Tokenize(source)));
        Assert.Equal(1, TextEditing.MatchingBracket(11, source, CSharpLexer.Tokenize(source)));
    }

    [Fact]
    public void Fuzzy_PrefersWordStartsAndRejectsNonSubsequences()
    {
        Assert.True(FuzzyMatcher.Score("bs", "Build Solution") > FuzzyMatcher.Score("bs", "Absolute"));
        Assert.Null(FuzzyMatcher.Score("xyz", "Build"));
        Assert.Equal(0, FuzzyMatcher.Score(string.Empty, "Build"));
    }

    [Fact]
    public void Framer_SplitsAndJoinsChunks()
    {
        var framer = new MessageFramer();
        byte[] stream = [.. MessageFramer.Frame(Encoding.UTF8.GetBytes("{\"a\":1}")), .. MessageFramer.Frame(Encoding.UTF8.GetBytes("{\"b\":2}"))];

        var first = framer.Append(stream.AsSpan(0, 10));
        var rest = framer.Append(stream.AsSpan(10));

        Assert.Empty(first);
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], rest.Select(b => Encoding.UTF8.GetString(b)));
    }

    private static List<(TokenKind Kind, string Text)> Kinds(string source) =>
        CSharpLexer.Tokenize(source).Select(t => (t.Kind, source.Substring(t.Start, t.Length))).ToList();
}
