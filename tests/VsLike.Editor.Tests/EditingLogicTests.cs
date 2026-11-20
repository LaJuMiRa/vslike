using VsLike.Editor.Editing;
using VsLike.Editor.Highlighting;

namespace VsLike.Editor.Tests;

public class EditingLogicTests
{
    [Theory]
    [InlineData('(', null, null, true)]
    [InlineData('(', null, 'x', false)]
    [InlineData('"', 'a', null, false)]
    [InlineData('"', ' ', ')', true)]
    [InlineData('x', null, null, false)]
    public void ShouldAutoClose(char typed, char? previous, char? next, bool expected)
    {
        Assert.Equal(expected, BracketPairing.ShouldAutoClose(typed, previous, next));
    }

    [Fact]
    public void OvertypeAndDeletePair()
    {
        Assert.True(BracketPairing.ShouldOvertype(')', ')'));
        Assert.False(BracketPairing.ShouldOvertype('(', '('));
        Assert.True(BracketPairing.ShouldDeletePair('[', ']'));
        Assert.False(BracketPairing.ShouldDeletePair('[', ')'));
    }

    [Fact]
    public void ToggleLineComments_AddsAtCommonIndentAndRemovesAgain()
    {
        string[] lines = ["    a();", string.Empty, "        b();"];

        var commented = CommentToggler.ToggleLineComments(lines, "//");
        Assert.Equal(["    // a();", string.Empty, "    //     b();"], commented);

        Assert.Equal(lines, CommentToggler.ToggleLineComments(commented, "//"));
    }

    [Fact]
    public void ToggleLineComments_MixedSelectionComments()
    {
        Assert.Equal(["// // a", "// b"], CommentToggler.ToggleLineComments(["// a", "b"], "//"));
    }

    [Fact]
    public void ToggleBlockComment_RoundTrips()
    {
        var wrapped = CommentToggler.ToggleBlockComment("<a />", "<!--", "-->");

        Assert.Equal("<!-- <a /> -->", wrapped);
        Assert.Equal("<a />", CommentToggler.ToggleBlockComment(wrapped, "<!--", "-->"));
    }

    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("App.csproj", "xml")]
    [InlineData("App.slnx", "xml")]
    [InlineData("settings.json", "json")]
    [InlineData("README.md", "markdown")]
    [InlineData("App.sln", "plaintext")]
    public void LanguageRegistry_MapsExtensions(string file, string language)
    {
        Assert.Equal(language, LanguageRegistry.FromPath(file).Id);
    }

    [Fact]
    public void Themes_AreEmbeddedAndParse()
    {
        Assert.NotNull(VsLikeRegistryOptions.GetTheme(EditorThemeKind.Dark).GetTokenColors());
        Assert.NotNull(VsLikeRegistryOptions.GetTheme(EditorThemeKind.Light).GetTokenColors());
    }
}
