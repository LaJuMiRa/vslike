using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.ProjectFiles;
using VsLike.Projects.Solutions;

namespace VsLike.App.SolutionExplorer;

/// <summary>
/// Solution Explorer: zeigt die geladene Solution und führt Datei-, Projekt- und Verweisoperationen aus.
/// csproj- und Solution-Änderungen laufen über das dotnet-CLI und laden die Solution anschließend neu.
/// </summary>
public sealed partial class SolutionExplorerViewModel : ObservableObject
{
    private readonly Workbench _workbench;

    [ObservableProperty]
    private SolutionModel? _solution;

    public SolutionExplorerViewModel(Workbench workbench)
    {
        _workbench = workbench;
    }

    public ObservableCollection<SolutionItemViewModel> Roots { get; } = [];

    public bool HasSolution => Solution is not null;

    private ProjectService Projects => _workbench.ProjectService;

    public void Load(string solutionPath)
    {
        Solution = SolutionReader.Read(solutionPath);
        Refresh();
    }

    public void Refresh()
    {
        if (Solution is null)
        {
            Roots.Clear();
            return;
        }

        var all = Roots.SelectMany(r => r.SelfAndDescendants()).ToList();
        var expanded = all.Where(n => n.IsExpanded).Select(n => n.Key)
            .Concat(all.Where(n => !n.IsExpanded).Select(n => "!" + n.Key))
            .ToHashSet(StringComparer.Ordinal);
        if (!File.Exists(Solution.FilePath))
        {
            Solution = null;
            Roots.Clear();
            OnPropertyChanged(nameof(HasSolution));
            return;
        }

        try
        {
            Solution = SolutionReader.Read(Solution.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            // Half-written file (e.g. during `dotnet sln add`): keep the last good tree; the next change refreshes again.
            return;
        }

        Roots.Clear();
        Roots.Add(SolutionTreeBuilder.Build(Solution, expanded));
        OnPropertyChanged(nameof(HasSolution));
    }

    public Task OpenAsync(SolutionItemViewModel item)
    {
        if (item.Kind == SolutionItemKind.File)
        {
            return _workbench.Documents.OpenAsync(item.Path);
        }

        item.IsExpanded = !item.IsExpanded;
        return Task.CompletedTask;
    }

    public async Task<string?> AddClassAsync(SolutionItemViewModel container)
    {
        if (container.ContainerDirectory is not { } directory || container.Project is not { } project)
        {
            return null;
        }

        var name = await _workbench.Dialogs.PromptAsync(Loc.Get("Command.Solution.AddClass"), Loc.Get("Prompt.ClassName"), "Class1").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var className = NamespaceResolver.ToIdentifier(name.Trim().Replace(".cs", string.Empty, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(directory, className + ".cs");
        if (File.Exists(path))
        {
            await ShowErrorAsync(Loc.Format("NewProject.Exists", Path.GetFileName(path))).ConfigureAwait(true);
            return null;
        }

        var @namespace = NamespaceResolver.Resolve(project, ProjectFile.Load(project).GetProperty("RootNamespace"), directory);
        var text = NamespaceResolver.ClassFile(@namespace, className);
        await File.WriteAllTextAsync(path, text).ConfigureAwait(true);
        _workbench.Intelligence.Workspace.AddDocument(path, text);
        Refresh();
        await _workbench.Documents.OpenAsync(path).ConfigureAwait(true);
        return path;
    }

    public async Task AddFolderAsync(SolutionItemViewModel container)
    {
        if (container.ContainerDirectory is not { } directory)
        {
            return;
        }

        var name = await _workbench.Dialogs.PromptAsync(Loc.Get("Command.Solution.AddFolder"), Loc.Get("Prompt.FolderName"), "NewFolder").ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            Directory.CreateDirectory(Path.Combine(directory, name.Trim()));
            container.IsExpanded = true;
            Refresh();
        }
    }

    public async Task RenameAsync(SolutionItemViewModel item)
    {
        if (item.Kind is not (SolutionItemKind.File or SolutionItemKind.Folder))
        {
            return;
        }

        var name = await _workbench.Dialogs.PromptAsync(Loc.Get("Command.Solution.Rename"), Loc.Get("Prompt.NewName"), item.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return;
        }

        var target = Path.Combine(Path.GetDirectoryName(item.Path)!, name.Trim());
        await MoveCoreAsync(item, target).ConfigureAwait(true);
    }

    /// <summary>Moves a file or folder into another folder or project (drag &amp; drop).</summary>
    public async Task<bool> MoveAsync(SolutionItemViewModel item, SolutionItemViewModel target)
    {
        if (item.Kind is not (SolutionItemKind.File or SolutionItemKind.Folder) || target.ContainerDirectory is not { } directory
            || target.Path.StartsWith(item.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.GetDirectoryName(item.Path) == directory)
        {
            return false;
        }

        return await MoveCoreAsync(item, Path.Combine(directory, item.Name)).ConfigureAwait(true);
    }

    public async Task DeleteAsync(SolutionItemViewModel item)
    {
        if (item.Kind is not (SolutionItemKind.File or SolutionItemKind.Folder)
            || !await _workbench.Dialogs.ConfirmAsync(Loc.Format("Confirm.Delete.Title", item.Name), Loc.Get("Confirm.Delete.Body")).ConfigureAwait(true))
        {
            return;
        }

        foreach (var document in _workbench.Documents.OpenDocuments.Where(d => d.FilePath is { } p && (p == item.Path || p.StartsWith(item.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal))).ToList())
        {
            document.MarkSaved(document.FilePath!);
            await _workbench.Documents.CloseAsync(document).ConfigureAwait(true);
        }

        foreach (var file in AffectedFiles(item.Path))
        {
            _workbench.Intelligence.Workspace.RemoveDocument(file);
        }

        if (item.Kind == SolutionItemKind.File)
        {
            File.Delete(item.Path);
        }
        else
        {
            Directory.Delete(item.Path, recursive: true);
        }

        Refresh();
    }

    public async Task AddExistingProjectAsync()
    {
        if (Solution is null || await _workbench.Dialogs.PickFileAsync("Command.Solution.AddExistingProject").ConfigureAwait(true) is not { } project)
        {
            return;
        }

        await RunAndReloadAsync(() => Projects.AddProjectToSolutionAsync(Solution.FilePath, project, null, CancellationToken.None)).ConfigureAwait(true);
    }

    public async Task RemoveProjectAsync(SolutionItemViewModel project)
    {
        if (Solution is null || project.Kind != SolutionItemKind.Project
            || !await _workbench.Dialogs.ConfirmAsync(Loc.Get("Command.Solution.RemoveProject"), Loc.Format("Confirm.RemoveProject", project.Name)).ConfigureAwait(true))
        {
            return;
        }

        await RunAndReloadAsync(() => Projects.RemoveProjectFromSolutionAsync(Solution.FilePath, project.Path, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>Sets the project references of <paramref name="project"/> to exactly <paramref name="references"/>.</summary>
    public async Task SetReferencesAsync(string project, IReadOnlyCollection<string> references)
    {
        var current = ProjectFile.Load(project).ProjectReferences();
        await RunAndReloadAsync(async () =>
        {
            foreach (var added in references.Except(current, StringComparer.Ordinal))
            {
                await Projects.AddProjectReferenceAsync(project, added, CancellationToken.None).ConfigureAwait(true);
            }

            foreach (var removed in current.Except(references, StringComparer.Ordinal))
            {
                await Projects.RemoveProjectReferenceAsync(project, removed, CancellationToken.None).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    public Task RemoveReferenceAsync(SolutionItemViewModel reference)
        => reference.Project is { } project
            ? RunAndReloadAsync(() => Projects.RemoveProjectReferenceAsync(project, reference.Path, CancellationToken.None))
            : Task.CompletedTask;

    public Task RemovePackageAsync(SolutionItemViewModel package)
        => package.Project is { } project
            ? RunAndReloadAsync(() => Projects.RemovePackageAsync(project, package.Path, CancellationToken.None))
            : Task.CompletedTask;

    /// <summary>Runs a project operation, shows CLI errors, then reloads explorer and workspace.</summary>
    public async Task<bool> RunAndReloadAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (ProjectOperationException ex)
        {
            await ShowErrorAsync(ex.Message + "\n\n" + ex.Output).ConfigureAwait(true);
            Refresh();
            return false;
        }

        Refresh();
        if (Solution is not null)
        {
            await _workbench.Intelligence.OpenSolutionAsync(Solution.FilePath).ConfigureAwait(true);
        }

        return true;
    }

    private static IEnumerable<string> AffectedFiles(string path)
        => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories) : [path];

    partial void OnSolutionChanged(SolutionModel? value) => OnPropertyChanged(nameof(HasSolution));

    private async Task<bool> MoveCoreAsync(SolutionItemViewModel item, string target)
    {
        if (File.Exists(target) || Directory.Exists(target))
        {
            await ShowErrorAsync(Loc.Format("NewProject.Exists", Path.GetFileName(target))).ConfigureAwait(true);
            return false;
        }

        var oldFiles = AffectedFiles(item.Path).ToList();
        if (item.Kind == SolutionItemKind.File)
        {
            File.Move(item.Path, target);
        }
        else
        {
            Directory.Move(item.Path, target);
        }

        foreach (var oldFile in oldFiles)
        {
            var newFile = item.Kind == SolutionItemKind.File ? target : Path.Combine(target, Path.GetRelativePath(item.Path, oldFile));
            _workbench.Intelligence.Workspace.RemoveDocument(oldFile);
            if (newFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                _workbench.Intelligence.Workspace.AddDocument(newFile, await File.ReadAllTextAsync(newFile).ConfigureAwait(true));
            }

            _workbench.Documents.OnFileMoved(oldFile, newFile);
        }

        Refresh();
        return true;
    }

    private Task ShowErrorAsync(string message) => _workbench.Dialogs.ShowMessageAsync(Loc.Get("Dialog.Error.Title"), message);
}
