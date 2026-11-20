using Avalonia.Headless.XUnit;
using VsLike.App.Intelligence;
using VsLike.App.Services;
using VsLike.Core.Code;

namespace VsLike.App.Tests;

public sealed class CodeIntelligenceTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();

    [AvaloniaFact]
    public async Task OpenSolution_TypingAnError_ShowsSquiggleAndProblemThatNavigates()
    {
        using var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(CodeIntelligence.NeedsRestore(_solution.SolutionPath));

        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        Assert.Equal(3, workbench.Intelligence.Workspace.Projects.Count);
        Assert.False(CodeIntelligence.NeedsRestore(_solution.SolutionPath));

        var carPath = _solution.PathOf("Lib1/Car.cs");
        await workbench.Documents.OpenAsync(carPath);
        var document = workbench.Documents.Active!;
        await UiWait.UntilAsync(() => document.Session is not null, TimeSpan.FromSeconds(5), "editor session");

        var text = document.TextDocument.Text;
        var offset = text.IndexOf("Start()", StringComparison.Ordinal);
        document.TextDocument.Replace(offset, "Start".Length, "Stop");

        await UiWait.UntilAsync(() => workbench.Problems.Items.Any(i => i.Diagnostic.Id == "CS1061"), TimeSpan.FromSeconds(10), "CS1061 in problems");
        await UiWait.UntilAsync(() => document.Session!.Diagnostics.Count > 0, TimeSpan.FromSeconds(5), "squiggle");
        Assert.Contains("1", workbench.Shell.ProblemsText, StringComparison.Ordinal);
        await UiWait.UntilAsync(() => document.Session!.Semantic.SpanCount > 0, TimeSpan.FromSeconds(10), "semantic spans");

        document.Session!.GoTo(1);
        var problem = workbench.Problems.Items.First(i => i.Diagnostic.Id == "CS1061");
        await workbench.Problems.OpenAsync(problem);
        Assert.Equal(problem.Diagnostic.StartLine, document.Session.Caret.Line);
        Assert.Equal(DiagnosticSeverityKind.Error, problem.Diagnostic.Severity);
    }

    public void Dispose()
    {
        _platform.Dispose();
        _solution.Dispose();
    }
}
