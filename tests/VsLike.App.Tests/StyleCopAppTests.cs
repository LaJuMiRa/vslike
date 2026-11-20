using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.Core.Settings;

namespace VsLike.App.Tests;

/// <summary>Abnahme Phase 7: StyleCop-Warnungen nur wenn an, live umschaltbar, Zustand pro Solution, globaler Standard.</summary>
public sealed class StyleCopAppTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();
    private readonly List<Workbench> _workbenches = [];

    [AvaloniaFact]
    public async Task Toggle_ShowsAndHidesStyleCopWarnings_AndIsRememberedPerSolution()
    {
        var workbench = await OpenAsync();
        Assert.False(workbench.StyleCop.IsEnabled);
        Assert.DoesNotContain(workbench.Problems.All, d => d.Category == "StyleCop");

        await workbench.StyleCop.ToggleAsync();
        await UiWait.UntilAsync(() => workbench.Problems.All.Any(d => d.Id == "SA1200"), TimeSpan.FromSeconds(15), "SA1200 in problems");
        Assert.Equal("StyleCop", workbench.Shell.AnalyzerText);
        Assert.True(SolutionSettings.Load(_solution.SolutionPath).StyleCopEnabled);
        workbench.Dispose();

        var reopened = await OpenAsync();
        Assert.True(reopened.StyleCop.IsEnabled);
        await UiWait.UntilAsync(() => reopened.Problems.All.Any(d => d.Category == "StyleCop"), TimeSpan.FromSeconds(15), "StyleCop after reopen");

        await reopened.StyleCop.ToggleAsync();
        await UiWait.UntilAsync(() => !reopened.Problems.All.Any(d => d.Category == "StyleCop"), TimeSpan.FromSeconds(15), "StyleCop removed");
        Assert.False(SolutionSettings.Load(_solution.SolutionPath).StyleCopEnabled);
    }

    [AvaloniaFact]
    public async Task GlobalDefault_AppliesToSolutionsWithoutOwnSetting()
    {
        var first = Track(new Workbench(new AppServices(_platform), new FakeDialogService(), () => null));
        first.StyleCop.ToggleDefault();
        first.Dispose();

        var workbench = await OpenAsync();

        Assert.True(workbench.StyleCop.IsEnabled);
        Assert.False(File.Exists(SolutionSettings.PathFor(_solution.SolutionPath)));
        await UiWait.UntilAsync(() => workbench.Problems.All.Any(d => d.Category == "StyleCop"), TimeSpan.FromSeconds(15), "StyleCop by default");
    }

    public void Dispose()
    {
        _workbenches.ForEach(w => w.Dispose());
        _platform.Dispose();
        _solution.Dispose();
    }

    private async Task<Workbench> OpenAsync()
    {
        var workbench = Track(new Workbench(new AppServices(_platform), new FakeDialogService(), () => null));
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        return workbench;
    }

    private Workbench Track(Workbench workbench)
    {
        _workbenches.Add(workbench);
        return workbench;
    }
}
