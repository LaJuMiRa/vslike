using VsLike.Projects.Templates;

namespace VsLike.Projects.Tests;

public class TemplateCatalogTests
{
    private const string Output = """
        These templates matched your input: --language='C#', --type='project'

        Template Name                                 Short Name           Language  Tags
        --------------------------------------------  -------------------  --------  ----------------------------------
        ASP.NET Core Web API                          webapi               C#,F#     Web/Web API/API/Service
        ASP.NET Core Web App (Razor Pages)            webapp,razor         C#        Web/MVC/Razor Pages
        Class Library                                 classlib             C#,F#,VB  Common/Library
        Console App                                   console              C#,F#,VB  Common/Console
        WPF Application                               wpf                  C#,VB     Common/WPF
        xUnit Test Project                            xunit                C#,F#,VB  Test/xUnit/Desktop/Web
        """;

    [Fact]
    public void Parse_ReadsColumnsSkipsWindowsOnlyAndOrdersPreferredFirst()
    {
        var templates = TemplateCatalog.Parse(Output);

        Assert.Equal(["console", "classlib", "xunit", "webapi", "webapp"], templates.Select(t => t.ShortName));
        Assert.Equal("Console App", templates[0].Name);
        Assert.Equal("Common/Console", templates[0].Tags);
    }

    [Fact]
    public void Parse_ReturnsEmptyForUnexpectedOutput()
    {
        Assert.Empty(TemplateCatalog.Parse("No templates found."));
    }
}
