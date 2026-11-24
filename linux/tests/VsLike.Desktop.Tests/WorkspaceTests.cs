using Avalonia.Headless.XUnit;
using VsLike.Kit;
using Xunit;

namespace VsLike.Desktop.Tests;

/// <summary>
/// The app workspace against the real engine (port of Tests/VsLikeTests/WorkspaceTests.swift). The diagram and editor
/// tests of the original belong to the diagram and editor parts.
/// </summary>
[Collection("Workspace")]
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), $"vslike-tests-{Guid.NewGuid()}.json");
    private readonly Preferences _preferences;

    public WorkspaceTests()
    {
        _preferences = new Preferences(_settingsFile);
    }

    public void Dispose() => File.Delete(_settingsFile);

    [AvaloniaFact(Timeout = 180_000)]
    public async Task Open_FolderPicksTheSolutionBuildsTreeAndSelectsStartupProject()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            Assert.Equal(3, workspace.Solution?.Projects.Count);
            Assert.Equal(["App", "Lib1", "Lib2"], workspace.Tree.Take(3).Select(n => n.Name));
            Assert.Equal("App", workspace.Build.StartupProjectName);
            Assert.Equal(3, workspace.Projects.Count);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 180_000)]
    public async Task Editing_PublishesLiveDiagnosticsAndQuickActionFixesThem()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            var document = Assert.IsType<CodeDocument>(workspace.OpenFile(Path.Combine(folder, "Lib1/Car.cs")));
            const string expression = "=> $\"{Name}: {_engine.Start()}\";";
            document.Replace(new TextRange(document.Text.IndexOf(expression, StringComparison.Ordinal), expression.Length), "=> Honk();");
            Assert.True(await Samples.Wait(() => workspace.Diagnostics.DiagnosticsFor(document.Path).Any(d => d.Code == "CS0103")));
            Assert.True(document.IsDirty);

            var offset = document.Text.IndexOf("Honk", StringComparison.Ordinal);
            var actions = await workspace.CodeActions(document, new TextRange(offset, 4));

            // "Generate method 'Honk'" (the title follows the system language).
            var generate = actions.First(a => a.Title.Contains("Honk", StringComparison.Ordinal)
                && !a.Title.Contains("lokal", StringComparison.OrdinalIgnoreCase) && !a.Title.Contains("local", StringComparison.OrdinalIgnoreCase));
            await workspace.Perform(generate);

            Assert.Contains("private string Honk()", document.Text);
            Assert.True(await Samples.Wait(() => !workspace.Diagnostics.DiagnosticsFor(document.Path).Any(d => d.Code == "CS0103")));
            document.UndoStack.Undo();
            Assert.DoesNotContain("private string Honk()", document.Text);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 240_000)]
    public async Task StyleCop_ToggleIsPersistedAndFixAllCleansTheFile()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            var document = Assert.IsType<CodeDocument>(workspace.OpenFile(Path.Combine(folder, "Lib2/Engine.cs")));
            document.Replace(new TextRange(0, document.Storage.TextLength), document.Text.Replace("{\n", "{   \n", StringComparison.Ordinal));

            await workspace.ToggleStyleCop();
            Assert.True(workspace.StyleCopEnabled);
            Assert.True(SolutionSettings.Load(workspace.SolutionPath!).StyleCopEnabled);
            Assert.True(await Samples.Wait(() => workspace.Diagnostics.DiagnosticsFor(document.Path).Any(d => d.Code == "SA1028")));

            await workspace.FixAllStyleCop(document);

            Assert.DoesNotContain("{   \n", document.Text);

            // SA1600: the undocumented property gets a comment in StyleCop's wording.
            Assert.Contains("/// Gets or initializes the horsepower.", document.Text);
            Assert.DoesNotContain(workspace.Diagnostics.DiagnosticsFor(document.Path), d => d.Code == "SA1600");
            Assert.True(workspace.Diagnostics.DiagnosticsFor(document.Path).Count(d => d.IsStyleCop) < 3);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 180_000)]
    public async Task StyleCopSettings_CreatedWithCompanyAndUsedForTheFileHeader()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            _preferences.StyleCopCompanyName = "Acme";
            await workspace.ToggleStyleCop();

            await workspace.OpenStyleCopSettings();

            // System.Text.Json writes `"key": value` (Foundation writes `"key" : value`).
            Assert.Contains("\"companyName\": \"Acme\"", await File.ReadAllTextAsync(Path.Combine(folder, "stylecop.json")));
            Assert.Equal("stylecop.json", workspace.ActiveDocument?.Name);
            var document = Assert.IsType<CodeDocument>(workspace.OpenFile(Path.Combine(folder, "Lib2/Engine.cs")));
            Assert.True(await Samples.Wait(() => workspace.Diagnostics.DiagnosticsFor(document.Path).Any(d => d.Code == "SA1633")));
            await workspace.FixAllStyleCop(document);
            Assert.Contains("Copyright (c) Acme. All rights reserved.", document.Text);
            Assert.DoesNotContain("PlaceholderCompany", document.Text);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 240_000)]
    public async Task Run_AndDebug_StartTheProgramInATerminalTabAndStopAtTheFirstLine()
    {
        Samples.UseMacAdapterIfNeeded();
        var (workspace, folder) = await OpenSample();
        try
        {
            workspace.Build.StartWithoutDebugging();
            var ran = await Samples.Wait(() => workspace.Terminals.Sessions.Any(s => s.IsRun && !s.IsRunning), 40);
            Assert.True(ran, $"sessions: {string.Join(", ", workspace.Terminals.Sessions.Select(s => $"{s.Title} running={s.IsRunning}"))} status: {workspace.Status.Text}");
            Assert.Equal(0, workspace.Terminals.Sessions.First(s => s.IsRun).ExitCode);
            Assert.Equal(BottomTab.Terminal, workspace.BottomTab);

            var program = Assert.IsType<CodeDocument>(workspace.OpenFile(Path.Combine(folder, "App/Program.cs")));
            program.ToggleBreakpoint(3);
            workspace.Debugger.BreakpointsChanged(program);
            await workspace.Debugger.Start();
            Assert.True(await Samples.Wait(() => workspace.Debugger.IsStopped, 120), workspace.Alert?.Message);
            Assert.Equal(3, workspace.Debugger.Frames.FirstOrDefault()?.Line);
            Assert.Equal(BottomTab.Debug, workspace.BottomTab);
            var debugTab = workspace.Terminals.Sessions[^1];
            Assert.True(debugTab.IsRun && debugTab.IsRunning);

            await workspace.Debugger.Resume();
            Assert.Equal(BottomTab.Terminal, workspace.BottomTab);
            Assert.True(await Samples.Wait(() => !debugTab.IsRunning, 30), string.Join("\n", workspace.Debugger.Console.Select(l => l.Text)));
            Assert.Equal(0, debugTab.ExitCode);
            await workspace.Debugger.Stop();
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 240_000)]
    public async Task Build_ReportsErrorsInProblemsAndSucceedsAfterFix()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            var program = Path.Combine(folder, "App/Program.cs");
            var original = await File.ReadAllTextAsync(program);
            await File.WriteAllTextAsync(program, original + "\nint broken = \"text\";\n");

            var failed = await workspace.Build.Run(BuildKind.Build);
            Assert.False(failed?.Succeeded);
            Assert.Contains(workspace.Diagnostics.Build, d => d.Code == "CS0029" && d.FilePath == program);
            Assert.Equal(StatusStyle.Error, workspace.Status.Style);

            await File.WriteAllTextAsync(program, original);
            var fixedResult = await workspace.Build.Run(BuildKind.Build);
            Assert.True(fixedResult?.Succeeded);
            Assert.Equal(StatusStyle.Success, workspace.Status.Style);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 240_000)]
    public async Task Tests_DiscoverAndRunInTheCalcSample()
    {
        var (workspace, folder) = await OpenSample("WithTests");
        try
        {
            Assert.Equal(["Calc.Tests"], workspace.Tests.TestProjects.Select(p => p.Name));

            workspace.Tests.Run();
            Assert.True(await Samples.Wait(() => !workspace.Tests.IsBusy && workspace.Tests.Results.Count > 0, 180));

            Assert.Equal(3, workspace.Tests.PassedCount);
            Assert.Equal(1, workspace.Tests.FailedCount);
            Assert.Equal(TestOutcome.Failed, workspace.Tests.Tree.FirstOrDefault()?.Outcome);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [AvaloniaFact(Timeout = 180_000)]
    public async Task ApplyEdit_ChangesClosedFilesOnDiskAndOpenDocumentsUndoably()
    {
        var (workspace, folder) = await OpenSample();
        try
        {
            var car = Path.Combine(folder, "Lib1/Car.cs");
            var engineFile = Path.Combine(folder, "Lib2/Engine.cs");
            var document = Assert.IsType<CodeDocument>(workspace.OpenFile(car));
            var openOffset = document.Text.IndexOf("Car\";", StringComparison.Ordinal);
            var closedOffset = (await File.ReadAllTextAsync(engineFile)).IndexOf("100", StringComparison.Ordinal);

            await workspace.Apply(
                new WorkspaceEdit([new FileEdit(car, [new TextEdit(openOffset, 3, "Auto")]), new FileEdit(engineFile, [new TextEdit(closedOffset, 3, "150")])]),
                "Test");

            Assert.Contains("\"Auto\";", document.Text);
            Assert.Contains("= 150;", await File.ReadAllTextAsync(engineFile));
            document.UndoStack.Undo();
            Assert.Contains("\"Car\";", document.Text);
        }
        finally
        {
            await Close(workspace, folder);
        }
    }

    [Fact]
    public void Localization_UsesGermanTranslationsAndFormatsPlaceholders()
    {
        var previous = L.Culture;
        try
        {
            L.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
            Assert.Equal("3 Projekte geladen", L.T("%lld projects loaded", 3));
            Assert.Equal("Basisklasse", L.T("Base Class"));
            L.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");
            Assert.Equal("3 projects loaded", L.T("%lld projects loaded", 3));
            Assert.Equal("Unknown {x} 5", L.T("Unknown {x} %d", 5));
        }
        finally
        {
            L.Culture = previous;
        }
    }

    private static async Task Close(Workspace workspace, string folder)
    {
        await workspace.CloseAll();
        Samples.Delete(folder);
    }

    private async Task<(Workspace Workspace, string Folder)> OpenSample(string name = "ThreeProjects")
    {
        var folder = await Samples.Copy(name);
        var workspace = new Workspace(_preferences) { ConfirmSaveHandler = _ => SaveChoice.Discard };
        await workspace.Open(folder);
        Assert.True(
            workspace.EngineStatus == EngineStatus.Ready,
            $"engine: {workspace.EngineStatus} {string.Join("\n", workspace.Output.LinesIn(OutputLog.Channel.Engine).Select(l => l.Text))}");
        return (workspace, folder);
    }
}
