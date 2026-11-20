using VsLike.Projects.Solutions;

namespace VsLike.Projects.Tests;

public class SolutionReaderTests
{
    [Fact]
    public void ParseSlnx_ReadsProjectsAndNestedFolders()
    {
        const string xml = "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/App/App.csproj\" />\n  </Folder>\n  <Folder Name=\"/src/libs/\">\n    <Project Path=\"src\\Lib\\Lib.csproj\" />\n  </Folder>\n  <Project Path=\"Tool/Tool.csproj\" />\n</Solution>";

        var model = SolutionReader.ParseSlnx("/repo/All.slnx", xml);

        Assert.Equal(SolutionFormat.Slnx, model.Format);
        Assert.Equal(["App", "Lib", "Tool"], model.Projects.Select(p => p.Name));
        Assert.Equal("/repo/src/Lib/Lib.csproj", model.Projects[1].FilePath);
        Assert.Equal(["/src/", "/src/libs/", "/"], model.Projects.Select(p => p.SolutionFolder));
    }

    [Fact]
    public void ParseSln_ReadsProjectsAndFolderNesting()
    {
        const string sln = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Tool", "Tool\Tool.csproj", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            Global
                GlobalSection(NestedProjects) = preSolution
                    {22222222-2222-2222-2222-222222222222} = {11111111-1111-1111-1111-111111111111}
                EndGlobalSection
            EndGlobal
            """;

        var model = SolutionReader.ParseSln("/repo/All.sln", sln);

        Assert.Equal(SolutionFormat.Sln, model.Format);
        Assert.Equal(["App", "Tool"], model.Projects.Select(p => p.Name));
        Assert.Equal("/src/", model.Projects[0].SolutionFolder);
        Assert.Equal("/", model.Projects[1].SolutionFolder);
        Assert.Equal("/repo/src/App/App.csproj", model.Projects[0].FilePath);
    }
}
