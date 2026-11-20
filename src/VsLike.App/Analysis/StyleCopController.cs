using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Core.Settings;
using VsLike.Projects;

namespace VsLike.App.Analysis;

/// <summary>
/// StyleCop-Schalter: pro Solution in <c>.vslike/settings.json</c>, sonst globaler Standard aus den App-Einstellungen.
/// Umschalten wirkt sofort (Analyzer werden ge- bzw. entladen und die Solution neu analysiert), ohne die csproj zu ändern.
/// </summary>
public sealed partial class StyleCopController : ObservableObject
{
    private readonly ICodeWorkspace _workspace;
    private readonly IDotnetCli _cli;
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly IDialogService _dialogs;
    private readonly ProjectService _projects;
    private string? _solutionPath;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    public StyleCopController(ICodeWorkspace workspace, IDotnetCli cli, AppServices services, ShellViewModel shell, IDialogService dialogs, ProjectService projects)
    {
        _workspace = workspace;
        _cli = cli;
        _services = services;
        _shell = shell;
        _dialogs = dialogs;
        _projects = projects;
    }

    public bool DefaultEnabled => _services.Settings.StyleCopEnabledByDefault;

    /// <summary>Applies the saved switch of a solution (or the global default) before it is analysed.</summary>
    public Task ApplyForSolutionAsync(string solutionPath)
    {
        _solutionPath = solutionPath;
        var enabled = SolutionSettings.Load(solutionPath).StyleCopEnabled ?? DefaultEnabled;
        return SetAsync(enabled, persist: false);
    }

    /// <summary>Switches StyleCop for the open solution (or for loose files when none is open) and remembers it.</summary>
    public Task ToggleAsync() => SetAsync(!IsEnabled, persist: true);

    public void ToggleDefault()
    {
        _services.UpdateSettings(s => s with { StyleCopEnabledByDefault = !s.StyleCopEnabledByDefault });
        OnPropertyChanged(nameof(DefaultEnabled));
        _shell.StatusText = Loc.Get(DefaultEnabled ? "StyleCop.DefaultOn" : "StyleCop.DefaultOff");
    }

    /// <summary>Optionally makes StyleCop part of the build: adds the package reference to every project (after confirmation).</summary>
    public async Task AddPackageReferencesAsync(IReadOnlyList<string> projectPaths)
    {
        if (projectPaths.Count == 0 || !await _dialogs.ConfirmAsync(Loc.Get("Command.StyleCop.AddPackage"), Loc.Format("StyleCop.AddPackageConfirm", projectPaths.Count, Roslyn.Analyzers.StyleCopPackage.Version)).ConfigureAwait(true))
        {
            return;
        }

        foreach (var project in projectPaths)
        {
            try
            {
                await _projects.AddPackageAsync(project, Roslyn.Analyzers.StyleCopPackage.Id, Roslyn.Analyzers.StyleCopPackage.Version, CancellationToken.None).ConfigureAwait(true);
            }
            catch (ProjectOperationException ex)
            {
                _shell.StatusText = ex.Message;
                return;
            }
        }

        _shell.StatusText = Loc.Format("StyleCop.PackageAdded", projectPaths.Count);
    }

    private async Task SetAsync(bool enabled, bool persist)
    {
        IsBusy = true;
        if (enabled)
        {
            _shell.StatusText = Loc.Get("StyleCop.Loading");
        }

        try
        {
            var ok = await Task.Run(() => _workspace.SetStyleCopEnabledAsync(enabled, _cli, CancellationToken.None)).ConfigureAwait(true);
            if (!ok)
            {
                _shell.StatusText = Loc.Format("StyleCop.Unavailable", Roslyn.Analyzers.StyleCopPackage.Version);
                IsEnabled = false;
                return;
            }

            IsEnabled = enabled;
            _shell.AnalyzerText = enabled ? "StyleCop" : string.Empty;
            if (persist)
            {
                _shell.StatusText = Loc.Get(enabled ? "StyleCop.On" : "StyleCop.Off");
                if (_solutionPath is not null)
                {
                    (SolutionSettings.Load(_solutionPath) with { StyleCopEnabled = enabled }).Save(_solutionPath);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
