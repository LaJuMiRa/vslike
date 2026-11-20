using VsLike.Editor.Folding;

namespace VsLike.Editor.Tests;

public class BraceFoldingTests
{
    [Fact]
    public void Compute_FoldsMultilineBlocksOnly()
    {
        const string code = "class A\n{\n    void M() { }\n    void N()\n    {\n    }\n}\n";

        var foldings = BraceFoldingStrategy.Compute(code);

        Assert.Equal(2, foldings.Count);
        Assert.Equal(code.IndexOf('{'), foldings[0].StartOffset);
        Assert.Equal(code.LastIndexOf('}') + 1, foldings[0].EndOffset);
    }

    [Fact]
    public void Compute_IgnoresBracesInStringsAndComments()
    {
        const string code = "var s = \"{\";\n// {\nvar v = @\"\"\"{\"\"\";\nvar c = '{';\n{\n}\n";

        var foldings = BraceFoldingStrategy.Compute(code);

        Assert.Single(foldings);
    }

    [Fact]
    public void Compute_FoldsRegionsAndBlockComments()
    {
        const string code = "#region Fields\nint a;\n#endregion\n/* one\n two */\n";

        var foldings = BraceFoldingStrategy.Compute(code);

        Assert.Contains(foldings, f => f.Name == "Fields");
        Assert.Contains(foldings, f => f.Name == "/* … */");
    }
}
