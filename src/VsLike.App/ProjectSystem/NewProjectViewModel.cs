using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.Solutions;
using VsLike.Projects.Templates;

namespace VsLike.App.ProjectSystem;

/// <summary>Formular „Neue Solution“ bzw. „Neues Projekt hinzufügen“.</summary>
public sealed partial class NewProjectViewModel : ObservableObject
{
    [ObservableProperty]
    private string _projectName = "MyApp";

    [ObservableProperty]
    private string _solutionName = "MyApp";

    [ObservableProperty]
    private string _location;

    [ObservableProperty]
    private ProjectTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _framework = "net10.0";

    [ObservableProperty]
    private bool _useSlnx = true;

    [ObservableProperty]
    private string _solutionFolder = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    private string _previousProjectName = "MyApp";

    public NewProjectViewModel(bool newSolution, string location, IReadOnlyList<ProjectTemplate> templates)
    {
        IsNewSolution = newSolution;
        _location = location;
        Templates = [.. templates];
        _selectedTemplate = Templates.FirstOrDefault();
    }

    public bool IsNewSolution { get; }

    public string Title => Loc.Get(IsNewSolution ? "NewProject.TitleSolution" : "NewProject.TitleProject");

    public ObservableCollection<ProjectTemplate> Templates { get; }

    public IReadOnlyList<string> Frameworks { get; } = ["net10.0", "net9.0", "net8.0"];

    public bool CanCreate => !IsBusy && SelectedTemplate is not null && IsValidName(ProjectName) && (!IsNewSolution || IsValidName(SolutionName));

    /// <summary>Gets the solution file path (for a new solution) after <see cref="CreateAsync"/>.</summary>
    public string? CreatedSolution { get; private set; }

    public string? CreatedProject { get; private set; }

    public static bool IsValidName(string name) => Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_.]*$") && !name.EndsWith('.');

    /// <summary>Creates the solution (optional) and the project and adds it to <paramref name="existingSolution"/> or the new one.</summary>
    public async Task<bool> CreateAsync(ProjectService projects, string? existingSolution, CancellationToken cancellationToken)
    {
        Error = string.Empty;
        if (!CanCreate)
        {
            Error = Loc.Get("NewProject.InvalidName");
            return false;
        }

        var solutionDirectory = IsNewSolution ? Path.Combine(Location, SolutionName) : Path.GetDirectoryName(existingSolution)!;
        if (IsNewSolution && Directory.Exists(solutionDirectory) && Directory.EnumerateFileSystemEntries(solutionDirectory).Any())
        {
            Error = Loc.Format("NewProject.Exists", solutionDirectory);
            return false;
        }

        if (Directory.Exists(Path.Combine(solutionDirectory, ProjectName)))
        {
            Error = Loc.Format("NewProject.Exists", ProjectName);
            return false;
        }

        IsBusy = true;
        try
        {
            var solution = IsNewSolution
                ? await projects.CreateSolutionAsync(solutionDirectory, SolutionName, UseSlnx ? SolutionFormat.Slnx : SolutionFormat.Sln, cancellationToken).ConfigureAwait(true)
                : existingSolution!;
            var project = await projects.CreateProjectAsync(SelectedTemplate!.ShortName, ProjectName, solutionDirectory, Framework, cancellationToken).ConfigureAwait(true);
            await projects.AddProjectToSolutionAsync(solution, project, SolutionFolder, cancellationToken).ConfigureAwait(true);
            CreatedSolution = solution;
            CreatedProject = project;
            return true;
        }
        catch (ProjectOperationException ex)
        {
            Error = ex.Message + "\n" + ex.Output;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnProjectNameChanged(string value)
    {
        if (IsNewSolution && (SolutionName.Length == 0 || SolutionName == _previousProjectName))
        {
            SolutionName = value;
        }

        _previousProjectName = value;
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnSolutionNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnSelectedTemplateChanged(ProjectTemplate? value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCreate));
}
