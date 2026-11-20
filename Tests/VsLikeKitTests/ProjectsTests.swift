import Foundation
import Testing
@testable import VsLikeKit

@Suite("Build output, solutions, project files, tests and templates")
struct ProjectsTests {
    func fixture(_ name: String) throws -> URL {
        try #require(Bundle.module.url(forResource: name, withExtension: nil, subdirectory: "Fixtures"))
    }

    @Test func buildOutput_parsesCanonicalErrorWithProject() throws {
        let line = "/src/App/Program.cs(12,5): error CS1002: ; expected [/src/App/App.csproj]"

        let diagnostic = try #require(BuildOutputParser.parse(line: line))

        #expect(diagnostic.code == "CS1002")
        #expect(diagnostic.severity == .error)
        #expect(diagnostic.filePath == "/src/App/Program.cs")
        #expect(diagnostic.startLine == 12 && diagnostic.startColumn == 5)
        #expect(diagnostic.message == "; expected")
        #expect(diagnostic.projectName == "App")
    }

    @Test func buildOutput_resolvesRelativeOriginAgainstProjectAndDeduplicates() {
        let lines = [
            OutputLine("Models/Car.cs(3,1): warning CS8618: Non-nullable property [/src/Lib/Lib.csproj]"),
            OutputLine("Build succeeded."),
            OutputLine("Models/Car.cs(3,1): warning CS8618: Non-nullable property [/src/Lib/Lib.csproj]"),
            OutputLine("/src/Lib/Lib.csproj : warning NU1603: Package resolved to a higher version"),
        ]

        let diagnostics = BuildOutputParser.parse(lines)

        #expect(diagnostics.count == 2)
        #expect(diagnostics[0].filePath == "/src/Lib/Models/Car.cs")
        #expect(diagnostics[1].code == "NU1603" && diagnostics[1].startLine == 1)
    }

    @Test func slnx_readsProjectsAndFolders() throws {
        let text = """
        <Solution>
          <Folder Name="/src/">
            <Project Path="src/App/App.csproj" />
          </Folder>
          <Project Path="Lib\\Lib.csproj" />
        </Solution>
        """

        let model = try SolutionReader.parseSlnx(path: "/work/Demo.slnx", text: text)

        #expect(model.name == "Demo")
        #expect(model.projects.map(\.name) == ["App", "Lib"])
        #expect(model.projects[0].filePath == "/work/src/App/App.csproj")
        #expect(model.projects[0].solutionFolder == "/src/")
        #expect(model.projects[1].filePath == "/work/Lib/Lib.csproj")
    }

    @Test func sln_readsNestedSolutionFolders() throws {
        let text = try String(contentsOf: fixture("sample.sln"), encoding: .utf8)

        let model = SolutionReader.parseSln(path: "/work/Demo.sln", text: text)

        #expect(model.projects.map(\.name) == ["App", "Tests"])
        #expect(model.projects[0].solutionFolder == "/src/")
        #expect(model.projects[0].filePath == "/work/src/App/App.csproj")
        #expect(model.projects[1].solutionFolder == "/")
        #expect(model.folders == ["/src/"])
    }

    @Test func projectFile_readsSetsAndRemovesPropertiesKeepingFormatting() {
        var project = ProjectFile(path: "/work/App/App.csproj", text: """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\\Lib\\Lib.csproj" />
            <PackageReference Include="Serilog" Version="4.0.0" />
          </ItemGroup>
        </Project>
        """)

        project.setProperty("TargetFramework", "net9.0")
        project.setProperty("Nullable", "enable")
        project.setProperty("OutputType", nil)

        #expect(project.property("TargetFramework") == "net9.0")
        #expect(project.property("Nullable") == "enable")
        #expect(project.property("OutputType") == nil)
        #expect(project.text.contains("    <Nullable>enable</Nullable>\n  </PropertyGroup>"))
        #expect(project.projectReferences() == ["/work/Lib/Lib.csproj"])
        #expect(project.packageReferences().map(\.id) == ["Serilog"])
        #expect(!project.isTestProject)
    }

    @Test func launchProfiles_parseWithCommentsAndTrailingCommas() {
        let json = """
        {
          // comment
          "profiles": {
            "App": { "commandName": "Project", "commandLineArgs": "--verbose", "environmentVariables": { "MODE": "dev" }, },
            "IIS Express": { "commandName": "IISExpress" }
          }
        }
        """

        let profiles = LaunchProfile.parse(Data(json.utf8))

        #expect(profiles.map(\.name) == ["App", "IIS Express"])
        #expect(profiles[0].isRunnable && !profiles[1].isRunnable)
        #expect(profiles[0].environment == ["MODE": "dev"])
        #expect(BuildService.runArguments(project: "/a/App.csproj", configuration: "Debug", profile: profiles[0]).suffix(2) == ["--launch-profile", "App"])
    }

    @Test func trx_parsesOutcomesDurationAndSourceFrame() throws {
        let results = TestService.parseTrx(try Data(contentsOf: fixture("sample.trx")))

        #expect(results.map(\.outcome) == [.passed, .failed, .skipped])
        #expect(results[1].duration == 1.5)
        #expect(results[1].errorMessage == "Assert.Equal() Failure")
        let frame = try #require(TestService.firstSourceFrame(results[1].stackTrace))
        #expect(frame.file == "/src/Calc.Tests/MathTests.cs" && frame.line == 21)
    }

    @Test func testList_takesIndentedNamesAfterTheLastHeader() {
        let output = ["Build succeeded.", "The following Tests are available:", "    Calc.Tests.A", "    Calc.Tests.B(x: 1)", "", "done"].map { OutputLine($0) }

        #expect(TestService.parseTestList(output) == ["Calc.Tests.A", "Calc.Tests.B(x: 1)"])
    }

    @Test func testNameAndFilter() {
        let name = TestName("Calc.Tests.MathTests.Add(a: 1)")

        #expect(name.namespace == "Calc.Tests" && name.className == "MathTests" && name.method == "Add")
        #expect(name.arguments == "(a: 1)")
        #expect(TestService.filter(exact: ["Calc.Tests.MathTests.Add(a: 1)"], prefixes: ["Calc.Other."]) == "FullyQualifiedName=Calc.Tests.MathTests.Add|FullyQualifiedName~Calc.Other.")
    }

    @Test func templates_parseTableAndPreferCommonOnes() {
        let output = """
        These templates matched your input: --type=project

        Template Name     Short Name        Language  Tags
        ----------------  ----------------  --------  --------------
        Class Library     classlib          [C#],F#   Common/Library
        Console App       console           [C#],F#   Common/Console
        WPF Application   wpf               [C#],VB   Common/WPF
        Worker Service    worker            [C#],F#   Common/Worker
        """

        let templates = ProjectService.parseTemplates(output)

        #expect(templates.map(\.shortName) == ["console", "classlib", "worker"])
        #expect(templates[0].name == "Console App")
    }

    @Test func packageList_parsesTopLevelPackages() {
        let json = """
        warning: something
        { "projects": [ { "frameworks": [ { "topLevelPackages": [ { "id": "Serilog", "requestedVersion": "4.0.0", "resolvedVersion": "4.0.0", "latestVersion": "4.1.0" } ] } ] } ] }
        """

        let packages = ProjectService.parsePackageList(json)

        #expect(packages.map(\.id) == ["Serilog"])
        #expect(packages[0].hasUpdate)
    }

    @Test func sdkList_parsesNewestFirst() {
        let sdks = DotnetLocator.parseSdks("9.0.100 [/usr/local/share/dotnet/sdk]\n10.0.402 [/usr/local/share/dotnet/sdk]\n10.0.100-rc.1 [/x]")

        #expect(sdks.map(\.fullVersion) == ["10.0.402", "10.0.100-rc.1", "9.0.100"])
        #expect(sdks[0].isSupported && !sdks[2].isSupported)
    }

    @Test func solutionSettings_roundTrip() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        let solution = folder.appendingPathComponent("Demo.slnx")
        var settings = SolutionSettings()
        settings.styleCopEnabled = true
        settings.breakpoints = ["/a.cs": [SourceBreakpoint(line: 3, condition: "x > 1")]]

        try settings.save(for: solution)

        #expect(SolutionSettings.load(for: solution) == settings)
    }
}
