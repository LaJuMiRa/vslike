using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.App.Testing;
using VsLike.Core.Code;
using VsLike.Core.Settings;
using VsLike.Projects.Build;
using VsLike.Projects.Testing;

namespace VsLike.App.Tests;

/// <summary>Abnahme Phase 6: Console-App baut und läuft, Fehlerklick springt zur Zeile; Test-Explorer.</summary>
public sealed class BuildRunTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly List<TestSolution> _solutions = [];

    [AvaloniaFact]
    public async Task BuildError_NavigatesToLine_ThenFixedAppRunsWithOutput()
    {
        var solution = Track(new TestSolution());
        using var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(solution.SolutionPath), workbench.Shell.StatusText);
        Assert.Equal(["App"], workbench.Build.StartupProjects.Select(p => p.Name));
        Assert.Equal("App", workbench.Build.StartupProject?.Name);

        var carPath = solution.PathOf("Lib1/Car.cs");
        await workbench.Documents.OpenAsync(carPath);
        var document = workbench.Documents.Active!;
        await UiWait.UntilAsync(() => document.Session is not null, TimeSpan.FromSeconds(5), "editor session");
        var text = document.TextDocument.Text;
        document.TextDocument.Insert(text.IndexOf("public string Drive", StringComparison.Ordinal), "public int Broken => \"x\";\n\n    ");

        var failed = await workbench.Build.BuildAsync(BuildKind.Build);

        Assert.NotNull(failed);
        Assert.False(failed.Succeeded);
        Assert.False(document.IsDirty);
        var error = Assert.Single(failed.Diagnostics, d => d.Severity == DiagnosticSeverityKind.Error);
        Assert.Equal(carPath, error.FilePath);
        Assert.Equal(11, error.StartLine);
        Assert.Contains(workbench.Output.Lines, l => l.IsNavigable && l.Diagnostic!.Id == error.Id);

        document.Session!.GoTo(1);
        await workbench.Problems.OpenAsync(workbench.Problems.Items.First(i => i.Diagnostic.FilePath == carPath && i.Diagnostic.Severity == DiagnosticSeverityKind.Error));
        Assert.Equal(11, document.Session.Caret.Line);

        document.TextDocument.Text = text;
        Assert.True(await workbench.Build.RunAsync(), string.Join('\n', workbench.Output.Lines.Select(l => l.Text)));
        Assert.Contains(workbench.Output.Lines, l => l.Text.StartsWith("Demo:", StringComparison.Ordinal));
        Assert.DoesNotContain(workbench.Problems.All, d => d.Category == "Build" && d.Severity == DiagnosticSeverityKind.Error);
        Assert.False(File.Exists(SolutionSettings.PathFor(solution.SolutionPath)), "opening and building must not write settings");
    }

    [AvaloniaFact]
    public async Task ConfigurationAndStartup_ArePersistedPerSolution()
    {
        var solution = Track(new TestSolution());
        using (var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null))
        {
            Assert.True(await workbench.OpenSolutionAsync(solution.SolutionPath));
            workbench.Build.Configuration = "Release";
            Assert.Equal(Path.Combine("App", "App.csproj"), SolutionSettings.Load(solution.SolutionPath).StartupProject);
            Assert.Null(workbench.Build.LaunchProfile?.Profile);
        }

        using var reopened = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        Assert.True(await reopened.OpenSolutionAsync(solution.SolutionPath));
        Assert.Equal("Release", reopened.Build.Configuration);
    }

    [AvaloniaFact]
    public async Task TestExplorer_DiscoversRunsAndNavigates()
    {
        var solution = Track(new TestSolution("WithTests"));
        using var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(Path.Combine(solution.Root, "Calc.slnx")), workbench.Shell.StatusText);
        var tests = workbench.Tests;
        Assert.Equal(["Calc.Tests"], tests.Roots.Select(r => r.Name));
        Assert.Empty(workbench.Build.StartupProjects);

        await tests.DiscoverAsync();
        Assert.Equal(4, tests.AllLeaves.Count());
        var theory = tests.Roots[0].SelfAndDescendants().Single(n => n.Kind == TestNodeKind.Method && n.Name == "Add_Zero");
        Assert.Equal(2, theory.Children.Count);

        await tests.RunAsync();
        Assert.Equal(TestOutcome.Failed, tests.Roots[0].Outcome);
        var wrong = Assert.Single(tests.AllLeaves, l => l.Outcome == TestOutcome.Failed);
        Assert.Equal("Add_Wrong", wrong.Name);
        Assert.Contains("Assert.Equal", wrong.Message, StringComparison.Ordinal);
        Assert.Equal(3, tests.AllLeaves.Count(l => l.Outcome == TestOutcome.Passed));

        await tests.OpenAsync(wrong);
        Assert.Equal(solution.PathOf("Calc.Tests/CalculatorTests.cs"), workbench.Documents.Active?.FilePath);
        await UiWait.UntilAsync(() => workbench.Documents.Active?.Session is not null, TimeSpan.FromSeconds(5), "editor session");
        Assert.Equal(9, workbench.Documents.Active!.Session!.Caret.Line);

        var passing = tests.AllLeaves.First(l => l.Name == "Add_Works");
        await tests.OpenAsync(passing);
        Assert.Equal(6, workbench.Documents.Active!.Session!.Caret.Line);

        File.WriteAllText(solution.PathOf("Calc.Tests/CalculatorTests.cs"), File.ReadAllText(solution.PathOf("Calc.Tests/CalculatorTests.cs")).Replace("Assert.Equal(4,", "Assert.Equal(3,", StringComparison.Ordinal));
        await tests.RunFailedAsync();
        Assert.Equal(TestOutcome.Passed, wrong.Outcome);
        Assert.Equal(TestOutcome.Passed, tests.Roots[0].Outcome);
    }

    public void Dispose()
    {
        _platform.Dispose();
        foreach (var solution in _solutions)
        {
            solution.Dispose();
        }
    }

    private TestSolution Track(TestSolution solution)
    {
        _solutions.Add(solution);
        return solution;
    }
}
