using VsLike.Projects.ProjectFiles;

namespace VsLike.Projects.Tests;

public class ProjectFileTests
{
    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">

          <!-- main settings -->
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <PropertyGroup Condition="'$(Configuration)' == 'Release'">
            <Optimize>true</Optimize>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\Lib\Lib.csproj" />
            <PackageReference Include="Serilog" Version="4.0.0" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void GetAndSetProperty_PreservesCommentsAndLayout()
    {
        var file = ProjectFile.Parse("/repo/App/App.csproj", Csproj);

        file.SetProperty("TargetFramework", "net9.0");
        file.SetProperty("Nullable", "enable");

        var xml = file.ToXml();
        Assert.Contains("<!-- main settings -->", xml, StringComparison.Ordinal);
        Assert.Contains("    <TargetFramework>net9.0</TargetFramework>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>", xml.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal("Exe", file.GetProperty("OutputType"));
        Assert.Null(file.GetProperty("Optimize"));
    }

    [Fact]
    public void SetProperty_WithEmptyValueRemovesIt()
    {
        var file = ProjectFile.Parse("/repo/App/App.csproj", Csproj);

        file.SetProperty("OutputType", null);

        Assert.Null(file.GetProperty("OutputType"));
        Assert.DoesNotContain("OutputType", file.ToXml(), StringComparison.Ordinal);
    }

    [Fact]
    public void References_AreResolvedToFullPaths()
    {
        var file = ProjectFile.Parse("/repo/App/App.csproj", Csproj);

        Assert.Equal(["/repo/Lib/Lib.csproj"], file.ProjectReferences());
        Assert.Equal([("Serilog", "4.0.0")], file.PackageReferences());
    }
}
