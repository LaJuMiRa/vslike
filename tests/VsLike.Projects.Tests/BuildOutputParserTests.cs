using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Projects.Build;

namespace VsLike.Projects.Tests;

public sealed class BuildOutputParserTests
{
    [Fact]
    public void CompilerError_WithPositionAndProject()
    {
        Assert.True(BuildOutputParser.TryParse("/src/App/Program.cs(12,5): error CS1002: ; erwartet. [/src/App/App.csproj]", out var d));
        Assert.Equal("CS1002", d!.Id);
        Assert.Equal("; erwartet.", d.Message);
        Assert.Equal(DiagnosticSeverityKind.Error, d.Severity);
        Assert.Equal("/src/App/Program.cs", d.FilePath);
        Assert.Equal((12, 5), (d.StartLine, d.StartColumn));
        Assert.Equal("App", d.ProjectName);
        Assert.Equal("Build", d.Category);
    }

    [Fact]
    public void Warning_WithFullRange()
    {
        Assert.True(BuildOutputParser.TryParse("  /src/A.cs(3,9,3,14): warning CS0168: The variable 'x' is declared but never used [/src/A.csproj]", out var d));
        Assert.Equal(DiagnosticSeverityKind.Warning, d!.Severity);
        Assert.Equal((3, 9), (d.StartLine, d.StartColumn));
    }

    [Fact]
    public void ProjectLevelDiagnostic_WithoutPosition()
    {
        Assert.True(BuildOutputParser.TryParse("/src/App/App.csproj : error NU1101: Unable to find package Foo. [/src/App/App.csproj]", out var d));
        Assert.Equal("NU1101", d!.Id);
        Assert.Equal("/src/App/App.csproj", d.FilePath);
        Assert.Equal(1, d.StartLine);
    }

    [Fact]
    public void MsBuildSwitchError_HasNoFile()
    {
        Assert.True(BuildOutputParser.TryParse("MSBUILD : error MSB1009: Project file does not exist.", out var d));
        Assert.Null(d!.FilePath);
        Assert.Equal("MSB1009", d.Id);
    }

    [Theory]
    [InlineData("  App -> /src/App/bin/Debug/net10.0/App.dll")]
    [InlineData("Fehler beim Buildvorgang.")]
    [InlineData("    1 Fehler")]
    [InlineData("Console.WriteLine(\"error CS0001: nope\");")]
    public void OrdinaryLines_AreIgnored(string line) => Assert.False(BuildOutputParser.TryParse(line, out _));

    [Fact]
    public void Parse_RemovesSummaryDuplicates()
    {
        var line = new OutputLine("/src/A.cs(1,1): error CS1002: ; expected [/src/A.csproj]", false);
        var other = new OutputLine("/src/A.cs(2,1): error CS1002: ; expected [/src/A.csproj]", false);
        Assert.Equal(2, BuildOutputParser.Parse([line, other, new OutputLine(string.Empty, false), line]).Count);
    }
}
