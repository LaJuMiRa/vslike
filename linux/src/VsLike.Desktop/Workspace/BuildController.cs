using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// Build, rebuild, clean and run (in the integrated terminal, so programs can read input). Holds the startup
/// project, configuration and launch profile shown in the toolbar.
/// </summary>
public sealed partial class BuildController : ObservableObject
{
    public static readonly IReadOnlyList<string> Configurations = ["Debug", "Release"];

    private readonly Workspace _workspace;
    private CancellationTokenSource? _task;
    private bool _restoring;

    public BuildController(Workspace workspace)
    {
        _workspace = workspace;
    }

    public enum BuildPhase
    {
        Idle,
        Building,
        Running,
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial BuildPhase Phase { get; private set; } = BuildPhase.Idle;

    [ObservableProperty]
    public partial BuildResult? LastResult { get; private set; }

    [ObservableProperty]
    public partial string Configuration { get; set; } = "Debug";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupProjectName))]
    public partial string? StartupProject { get; set; }

    [ObservableProperty]
    public partial string? LaunchProfile { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<LaunchProfile> Profiles { get; private set; } = [];

    /// <summary>Gets the projects that can be started (executables and test-less apps).</summary>
    public IReadOnlyList<SolutionProject> RunnableProjects =>
        [.. (_workspace.Solution?.Projects ?? []).Where(IsRunnable)];

    public string? StartupProjectName => StartupProject is null ? null : Path.GetFileNameWithoutExtension(StartupProject);

    public bool IsBusy => Phase != BuildPhase.Idle;

    public string? BuildTarget => _workspace.SolutionPath;

    public void RestoreSelection()
    {
        var settings = _workspace.Settings;
        _restoring = true;
        Configuration = settings.Configuration ?? "Debug";
        var runnable = RunnableProjects;
        StartupProject = runnable.FirstOrDefault(p => p.FilePath == settings.StartupProject)?.FilePath ?? runnable.FirstOrDefault()?.FilePath;
        LaunchProfile = (settings.LaunchProfile is { } name ? Profiles.FirstOrDefault(p => p.Name == name)?.Name : null)
            ?? Profiles.FirstOrDefault(p => p.IsRunnable)?.Name;
        _restoring = false;
        Persist();
    }

    public void Cancel()
    {
        _task?.Cancel();
        _task = null;
    }

    public async Task<BuildResult?> Run(BuildKind kind, CancellationToken cancellationToken = default)
    {
        if (BuildTarget is not { } target || Phase != BuildPhase.Idle)
        {
            return null;
        }

        _workspace.SaveAll();
        Phase = BuildPhase.Building;
        _workspace.Output.Clear(OutputLog.Channel.Build);
        _workspace.Output.SelectedChannel = OutputLog.Channel.Build;
        var verb = kind switch
        {
            BuildKind.Build => L.T("Building"),
            BuildKind.Rebuild => L.T("Rebuilding"),
            _ => L.T("Cleaning"),
        };
        _workspace.Status = new StatusMessage($"{verb} {Path.GetFileName(target)}…", StatusStyle.Progress);
        try
        {
            var service = new BuildService(CommandRunner.Dotnet(_workspace.Preferences.EffectiveLanguage));
            var result = await service.Build(target, Configuration, kind, _workspace.Output.Sink(OutputLog.Channel.Build), cancellationToken);
            LastResult = result;
            _workspace.Diagnostics.SetBuild(result.Diagnostics);
            Report(kind, result);
            return result;
        }
        catch (Exception ex) when (ex is CommandException or IOException or System.ComponentModel.Win32Exception)
        {
            _workspace.Status = new StatusMessage(ex.Message, StatusStyle.Error);
            _workspace.Output.Append(ex.Message, OutputLog.Channel.Build, isError: true);
            return null;
        }
        finally
        {
            Phase = BuildPhase.Idle;
        }
    }

    public void Start(BuildKind kind)
    {
        if (_task is not null)
        {
            return;
        }

        var task = new CancellationTokenSource();
        _task = task;
        _ = Finish(Run(kind, task.Token), task);
    }

    /// <summary>Builds, then starts the startup project in a terminal tab (input works, output keeps colours).</summary>
    public void StartWithoutDebugging()
    {
        if (_task is not null)
        {
            return;
        }

        var task = new CancellationTokenSource();
        _task = task;
        _ = Finish(BuildAndRun(task.Token), task);
    }

    private static bool IsRunnable(SolutionProject project)
    {
        try
        {
            var file = ProjectFile.Load(project.FilePath);
            var output = file.Property("OutputType")?.ToLowerInvariant();
            var web = file.Text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);
            return (output is "exe" or "winexe" || web) && !file.IsTestProject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task Finish(Task work, CancellationTokenSource task)
    {
        try
        {
            await work;
        }
        finally
        {
            if (ReferenceEquals(_task, task))
            {
                _task = null;
            }
        }
    }

    private async Task BuildAndRun(CancellationToken cancellationToken)
    {
        if (StartupProject is not { } project)
        {
            _workspace.Alert = new AlertMessage(L.T("Run"), L.T("This solution has no project that can be started."));
            return;
        }

        if (await Run(BuildKind.Build, cancellationToken) is not { Succeeded: true })
        {
            return;
        }

        var profile = Profiles.FirstOrDefault(p => p.Name == LaunchProfile);
        var arguments = BuildService.RunArguments(project, Configuration, profile);
        var directory = BuildService.WorkingDirectory(project, profile);
        await _workspace.Terminals.Run(L.T("Run: %@", StartupProjectName ?? string.Empty), arguments, directory);
        _workspace.Status = new StatusMessage(L.T("Running %@", StartupProjectName ?? string.Empty), StatusStyle.Progress);
    }

    private void Report(BuildKind kind, BuildResult result)
    {
        if (result.Process.WasCancelled)
        {
            _workspace.Status = new StatusMessage(L.T("Build cancelled"), StatusStyle.Warning);
        }
        else if (result.Succeeded)
        {
            var time = result.Duration.TotalSeconds.ToString("0.0", L.Culture);
            var text = kind == BuildKind.Clean
                ? L.T("Clean succeeded")
                : L.T("Build succeeded · %lld warnings · %@ s", result.WarningCount, time);
            _workspace.Status = new StatusMessage(text, StatusStyle.Success);
        }
        else
        {
            _workspace.Status = new StatusMessage(L.T("Build failed · %lld errors", result.ErrorCount), StatusStyle.Error);
            _workspace.BottomTab = BottomTab.Problems;
            _workspace.IsBottomPanelVisible = true;
        }
    }

    private void RefreshProfiles()
    {
        Profiles = StartupProject is null ? [] : [.. VsLike.Kit.LaunchProfile.Read(StartupProject).Where(p => p.IsRunnable)];
        if (LaunchProfile is { } current && !Profiles.Any(p => p.Name == current))
        {
            LaunchProfile = Profiles.FirstOrDefault()?.Name;
        }
    }

    private void Persist()
    {
        if (_restoring || _workspace.SolutionPath is null)
        {
            return;
        }

        var settings = _workspace.Settings;
        if (settings.Configuration == Configuration && settings.StartupProject == StartupProject && settings.LaunchProfile == LaunchProfile)
        {
            return;
        }

        // Only remember choices that differ from the defaults, so that a fresh solution gets no settings file.
        var isDefault = Configuration == "Debug" && StartupProject == RunnableProjects.FirstOrDefault()?.FilePath
            && (LaunchProfile is null || LaunchProfile == Profiles.FirstOrDefault()?.Name);
        if (isDefault && settings == new SolutionSettings())
        {
            return;
        }

        _workspace.Settings = settings with { Configuration = Configuration, StartupProject = StartupProject, LaunchProfile = LaunchProfile };
        _workspace.TrySaveSettings();
    }

    partial void OnConfigurationChanged(string value) => Persist();

    partial void OnStartupProjectChanged(string? value)
    {
        RefreshProfiles();
        Persist();
    }

    partial void OnLaunchProfileChanged(string? value) => Persist();
}
