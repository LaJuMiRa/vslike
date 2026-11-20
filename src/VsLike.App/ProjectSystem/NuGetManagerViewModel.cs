using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.NuGet;

namespace VsLike.App.ProjectSystem;

/// <summary>NuGet-Verwaltung für ein Projekt: Suchen, Installieren, Aktualisieren, Entfernen (öffnet als Tab).</summary>
public sealed partial class NuGetManagerViewModel : Document
{
    private readonly NuGetClient _client;
    private readonly ProjectService _projects;
    private readonly Func<Func<Task>, Task<bool>> _runAndReload;
    private CancellationTokenSource? _search;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _includePrerelease;

    [ObservableProperty]
    private NuGetPackage? _selectedPackage;

    [ObservableProperty]
    private InstalledPackage? _selectedInstalled;

    [ObservableProperty]
    private string? _selectedVersion;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public NuGetManagerViewModel(string projectPath, NuGetClient client, ProjectService projects, Func<Func<Task>, Task<bool>> runAndReload)
    {
        ProjectPath = projectPath;
        _client = client;
        _projects = projects;
        _runAndReload = runAndReload;
        Id = "nuget:" + projectPath;
        Title = Loc.Format("NuGet.Title", Path.GetFileNameWithoutExtension(projectPath));
    }

    public string ProjectPath { get; }

    public ObservableCollection<NuGetPackage> Results { get; } = [];

    public ObservableCollection<InstalledPackage> Installed { get; } = [];

    public ObservableCollection<string> Versions { get; } = [];

    [RelayCommand]
    public async Task SearchAsync()
    {
        _search?.Cancel();
        var cancellation = _search = new CancellationTokenSource();
        try
        {
            var results = await _client.SearchAsync(Query, IncludePrerelease, 0, 30, cancellation.Token).ConfigureAwait(true);
            if (!cancellation.IsCancellationRequested)
            {
                Results.Clear();
                foreach (var result in results)
                {
                    Results.Add(result);
                }

                Status = string.Empty;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Status = Loc.Format("NuGet.Error", ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Neuere Suche läuft.
        }
    }

    [RelayCommand]
    public async Task LoadInstalledAsync()
    {
        try
        {
            var packages = await _projects.ListPackagesAsync(ProjectPath, outdated: true, CancellationToken.None).ConfigureAwait(true);
            Installed.Clear();
            foreach (var package in packages)
            {
                Installed.Add(package);
            }
        }
        catch (ProjectOperationException ex)
        {
            Status = Loc.Format("NuGet.Error", ex.Output.Split('\n').LastOrDefault(l => l.Length > 0) ?? ex.Message);
        }
    }

    [RelayCommand]
    public Task InstallAsync()
        => SelectedPackage is { } package ? RunAsync("NuGet.Install", package.Id, () => _projects.AddPackageAsync(ProjectPath, package.Id, SelectedVersion, CancellationToken.None)) : Task.CompletedTask;

    [RelayCommand]
    public Task UpdateAsync()
        => SelectedInstalled is { LatestVersion: { } latest } package ? RunAsync("NuGet.Update", package.Id, () => _projects.AddPackageAsync(ProjectPath, package.Id, latest, CancellationToken.None)) : Task.CompletedTask;

    [RelayCommand]
    public Task UninstallAsync()
        => SelectedInstalled is { } package ? RunAsync("NuGet.Uninstall", package.Id, () => _projects.RemovePackageAsync(ProjectPath, package.Id, CancellationToken.None)) : Task.CompletedTask;

    partial void OnSelectedPackageChanged(NuGetPackage? value) => _ = LoadVersionsAsync(value);

    private async Task LoadVersionsAsync(NuGetPackage? package)
    {
        Versions.Clear();
        SelectedVersion = null;
        if (package is null)
        {
            return;
        }

        try
        {
            foreach (var version in await _client.GetVersionsAsync(package.Id, IncludePrerelease, CancellationToken.None).ConfigureAwait(true))
            {
                Versions.Add(version);
            }
        }
        catch (HttpRequestException)
        {
            Versions.Add(package.LatestVersion);
        }

        SelectedVersion = Versions.FirstOrDefault();
    }

    private async Task RunAsync(string actionKey, string packageId, Func<Task> operation)
    {
        IsBusy = true;
        Status = Loc.Get("NuGet.Working");
        try
        {
            if (await _runAndReload(operation).ConfigureAwait(true))
            {
                Status = Loc.Format("NuGet.Done", Loc.Get(actionKey), packageId);
            }

            await LoadInstalledAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
