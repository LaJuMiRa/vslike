using Avalonia.Headless.XUnit;
using VsLike.App.Services;

namespace VsLike.App.Tests;

/// <summary>Abnahme Phase 9 in der IDE: Breakpoint trifft, Variablen und Überwachung sichtbar, Stepping, Konsole.</summary>
public sealed class DebuggerAppTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();

    [AvaloniaFact]
    public async Task Breakpoint_Hits_ShowsVariables_StepsAndContinues()
    {
        using var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        var car = _solution.PathOf("Lib1/Car.cs");
        var debug = workbench.Debugger;
        debug.ToggleBreakpoint(car, 11);
        await debug.AddWatchAsync("Name");

        await debug.StartOrContinueAsync();
        await UiWait.UntilAsync(() => debug.IsStopped, TimeSpan.FromSeconds(60), "breakpoint hit: " + workbench.Shell.StatusText);

        var frame = debug.SelectedFrame!;
        Assert.Equal((car, 11), (frame.FilePath, frame.Line));
        Assert.Contains("Drive", frame.Name, StringComparison.Ordinal);
        Assert.True(debug.Locals.SelectMany(s => s.Children).Any(v => v.Name == "this"), string.Join(" | ", debug.Locals.Select(s => $"{s.Name}#{s.Reference}:[{string.Join(",", s.Children.Select(c => c.Display))}]")));
        Assert.Equal("\"Demo\"", Assert.Single(debug.Watches).Value);
        Assert.Equal(car, workbench.Documents.Active?.FilePath);
        await UiWait.UntilAsync(() => workbench.Documents.Active?.Session is not null, TimeSpan.FromSeconds(5), "editor session");
        Assert.Equal(11, workbench.Documents.Active!.Session!.BreakpointMargin.ExecutionLine);
        var text = workbench.Documents.Active.TextDocument.Text;
        var hover = await debug.HoverAsync(workbench.Documents.Active, text.IndexOf("Name}", StringComparison.Ordinal) + 1);
        Assert.Equal("Name = \"Demo\"", hover?.Signature);

        await debug.StepOverAsync();
        await UiWait.UntilAsync(() => debug.IsStopped, TimeSpan.FromSeconds(30), "step over");
        Assert.NotNull(debug.SelectedFrame);

        debug.ToggleBreakpoint(car, 11);
        await debug.StartOrContinueAsync();
        await UiWait.UntilAsync(() => !debug.IsDebugging, TimeSpan.FromSeconds(30), "program exit");
        Assert.Contains(debug.Console, l => l.StartsWith("Demo:", StringComparison.Ordinal));
        Assert.Null(workbench.Documents.Active.Session.BreakpointMargin.ExecutionLine);
    }

    [Fact]
    public void Arguments_AreSplitRespectingQuotes()
        => Assert.Equal(["a", "b c", "d"], VsLike.App.Debugging.DebugController.SplitArguments("a \"b c\"  d"));

    public void Dispose()
    {
        _platform.Dispose();
        _solution.Dispose();
    }
}
