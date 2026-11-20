using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.App.Output;
using VsLike.App.Problems;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Core.Settings;
using VsLike.Projects.Build;
using VsLike.Projects.ProjectFiles;
using VsLike.Projects.Testing;

namespace VsLike.App.Build;

/// <summary>
/// Build/Rebuild/Clean/Start der geladenen Solution: Konfiguration, Startprojekt und Startprofil (pro Solution
/// gespeichert), Live-Ausgabe im Ausgabefenster, Build-Diagnosen in der Fehlerliste, Abbrechen.
/// </summary>
public sealed partial class BuildController : ObservableObject
{
    private readonly BuildService _service;
    private readonly OutputViewModel _output;
    private readonly ProblemsViewModel _problems;
    private readonly ShellViewModel _shell;
    private readonly Func<Task> _saveAll;
    private CancellationTokenSource? _cancellation;
    private string? _solutionPath;
    private bool _loading;

    [ObservableProperty]
    private string _configuration = "Debug";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private StartupProjectItem? _startupProject;

    [ObservableProperty]
    private LaunchProfileItem? _launchProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanRun), nameof(CanBuild))]
    private bool _isBuilding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanRun), nameof(CanBuild))]
    private bool _isRunning;

    public BuildController(BuildService service, OutputViewModel output, ProblemsViewModel problems, ShellViewModel shell, Func<Task> saveAll)
    {
        _service = service;
        _output = output;
        _problems = problems;
        _shell = shell;
        _saveAll = saveAll;
    }

    public static IReadOnlyList<string> Configurations { get; } = ["Debug", "Release"];

    public ObservableCollection<StartupProjectItem> StartupProjects { get; } = [];

    public ObservableCollection<LaunchProfileItem> LaunchProfiles { get; } = [];

    public string? SolutionPath => _solutionPath;

    public bool HasSolution => _solutionPath is not null;

    public bool IsBusy => IsBuilding || IsRunning;

    public bool CanBuild => HasSolution && !IsBusy;

    public bool CanRun => StartupProject is not null && !IsBusy;

    /// <summary>Console, WinExe, Web and Worker projects can be started; test projects cannot.</summary>
    public static bool IsRunnable(string projectPath)
    {
        try
        {
            var project = ProjectFile.Load(projectPath);
            var outputType = project.GetProperty("OutputType");
            var sdk = File.ReadAllText(projectPath);
            var isExe = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) || string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
                || (outputType is null && (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) || sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase)));
            return isExe && !TestService.IsTestProject(projectPath);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads the runnable projects and the saved selection of a solution (or a single .csproj).</summary>
    public void Load(string solutionPath, IEnumerable<string> projectPaths)
    {
        _loading = true;
        try
        {
            _solutionPath = solutionPath;
            var settings = SolutionSettings.Load(solutionPath);
            Configuration = Configurations.Contains(settings.Configuration) ? settings.Configuration : "Debug";
            StartupProjects.Clear();
            foreach (var path in projectPaths.Where(IsRunnable).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase))
            {
                StartupProjects.Add(new StartupProjectItem(Path.GetFileNameWithoutExtension(path), path));
            }

            var saved = settings.StartupProject is { } relative ? Path.GetFullPath(relative, Path.GetDirectoryName(solutionPath)!) : null;
            StartupProject = StartupProjects.FirstOrDefault(p => p.Path == saved) ?? StartupProjects.FirstOrDefault();
            LoadProfiles(settings.LaunchProfile);
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(HasSolution));
        OnPropertyChanged(nameof(CanBuild));
    }

    public void SetStartupProject(string projectPath)
    {
        if (StartupProjects.FirstOrDefault(p => p.Path == projectPath) is { } item)
        {
            StartupProject = item;
        }
        else if (IsRunnable(projectPath))
        {
            item = new StartupProjectItem(Path.GetFileNameWithoutExtension(projectPath), projectPath);
            StartupProjects.Add(item);
            StartupProject = item;
        }
    }

    /// <summary>Builds the solution (or one project); returns null when another build or run is in progress.</summary>
    public async Task<BuildResult?> BuildAsync(BuildKind kind, string? project = null)
    {
        if (_solutionPath is null || IsBusy)
        {
            return null;
        }

        var target = project ?? _solutionPath;
        var name = Path.GetFileName(target);
        await _saveAll().ConfigureAwait(true);
        using var cancellation = _cancellation = new CancellationTokenSource();
        IsBuilding = true;
        _output.Begin(Loc.Format(kind switch { BuildKind.Clean => "Build.CleanStarted", BuildKind.Rebuild => "Build.RebuildStarted", _ => "Build.Started" }, name, Configuration));
        _shell.ShowPanel(ShellPanels.Output.Id);
        _shell.StatusText = Loc.Format("Build.Running", name);
        try
        {
            var result = await Task.Run(() => _service.BuildAsync(target, Configuration, kind, _output.Append, cancellation.Token), CancellationToken.None).ConfigureAwait(true);
            _problems.ReplaceSource(ProblemsViewModel.BuildSource, result.Diagnostics);
            var summary = result.WasCancelled
                ? Loc.Get("Build.Cancelled")
                : Loc.Format(result.Succeeded ? "Build.Succeeded" : "Build.Failed", result.ErrorCount, result.WarningCount, result.Duration.TotalSeconds);
            _output.AppendText(summary, !result.Succeeded);
            _output.Flush();
            _shell.StatusText = summary;
            if (!result.Succeeded && result.ErrorCount > 0)
            {
                _shell.ShowPanel(ShellPanels.Problems.Id);
            }

            return result;
        }
        finally
        {
            _cancellation = null;
            IsBuilding = false;
        }
    }

    /// <summary>Builds the startup project and starts it without debugger.</summary>
    public async Task<bool> RunAsync()
    {
        if (StartupProject is not { } startup || IsBusy)
        {
            if (StartupProject is null)
            {
                _shell.StatusText = Loc.Get("Run.NoStartupProject");
            }

            return false;
        }

        var build = await BuildAsync(BuildKind.Build, startup.Path).ConfigureAwait(true);
        if (build is not { Succeeded: true })
        {
            return false;
        }

        using var cancellation = _cancellation = new CancellationTokenSource();
        IsRunning = true;
        var profile = LaunchProfile?.Profile;
        _output.AppendText(Loc.Format("Run.Starting", startup.Name, profile?.Name ?? Loc.Get("Run.NoProfile")));
        _shell.StatusText = Loc.Format("Run.Running", startup.Name);
        try
        {
            var result = await Task.Run(() => _service.RunAsync(startup.Path, Configuration, profile, _output.Append, cancellation.Token), CancellationToken.None).ConfigureAwait(true);
            var summary = result.WasCancelled ? Loc.Format("Run.Stopped", startup.Name) : Loc.Format("Run.Exited", startup.Name, result.ExitCode);
            _output.AppendText(summary, !result.Succeeded && !result.WasCancelled);
            _output.Flush();
            _shell.StatusText = summary;
            return result.Succeeded;
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>Cancels the running build or stops the started program (whole process tree).</summary>
    public void Stop() => _cancellation?.Cancel();

    partial void OnConfigurationChanged(string value) => Persist();

    partial void OnStartupProjectChanged(StartupProjectItem? value)
    {
        if (!_loading)
        {
            LoadProfiles(null);
            Persist();
        }
    }

    partial void OnLaunchProfileChanged(LaunchProfileItem? value) => Persist();

    private void LoadProfiles(string? preferred)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            LaunchProfiles.Clear();
            if (StartupProject is { } startup)
            {
                foreach (var profile in LaunchSettingsReader.Read(startup.Path).Where(p => p.IsRunnable))
                {
                    LaunchProfiles.Add(new LaunchProfileItem(profile.Name, profile));
                }
            }

            LaunchProfiles.Add(new LaunchProfileItem(Loc.Get("Run.NoProfile"), null));
            LaunchProfile = LaunchProfiles.FirstOrDefault(p => p.Profile?.Name == preferred) ?? LaunchProfiles[0];
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void Persist()
    {
        if (_loading || _solutionPath is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_solutionPath)!;
        try
        {
            var settings = SolutionSettings.Load(_solutionPath) with
            {
                Configuration = Configuration,
                StartupProject = StartupProject is null ? null : Path.GetRelativePath(directory, StartupProject.Path),
                LaunchProfile = LaunchProfile?.Profile?.Name,
            };
            settings.Save(_solutionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.StatusText = ex.Message;
        }
    }
}
