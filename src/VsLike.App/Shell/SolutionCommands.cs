using Avalonia.Controls;
using VsLike.App.ProjectSystem;
using VsLike.App.SolutionExplorer;
using VsLike.Core.Commands;
using VsLike.Projects.ProjectFiles;

namespace VsLike.App.Shell;

/// <summary>Befehle für Solutions und Projekte sowie das Kontextmenü des Solution Explorers.</summary>
public static class SolutionCommands
{
    public static void Register(Workbench workbench)
    {
        var explorer = workbench.SolutionExplorer;
        Add(workbench, "file.newSolution", "Command.File.NewSolution", "Category.File", "Cmd+Shift+N", () => NewProjectAsync(workbench, newSolution: true));
        Add(workbench, "solution.addNewProject", "Command.Solution.AddNewProject", "Category.File", null, () => NewProjectAsync(workbench, newSolution: false), () => explorer.HasSolution);
        Add(workbench, "solution.addExistingProject", "Command.Solution.AddExistingProject", "Category.File", null, explorer.AddExistingProjectAsync, () => explorer.HasSolution);
        Add(workbench, "solution.reload", "Command.Solution.Reload", "Category.File", null, () => explorer.RunAndReloadAsync(() => Task.CompletedTask), () => explorer.HasSolution);
        var menus = workbench.Services.Menus;
        menus.Add("Menu.File", "file.newSolution");
        menus.Add("Menu.File", "solution.addNewProject", group: 1);
        menus.Add("Menu.File", "solution.addExistingProject", group: 1);
        menus.Add("Menu.File", "solution.reload", group: 1);
        SolutionExplorerView.MenuProvider = item => MenuFor(workbench, item);
        ProjectPropertiesView.Saved = async vm =>
        {
            workbench.Shell.StatusText = VsLike.Core.Localization.Loc.Format("Properties.Saved", Path.GetFileNameWithoutExtension(vm.ProjectPath));
            await explorer.RunAndReloadAsync(() => Task.CompletedTask).ConfigureAwait(true);
        };
    }

    public static async Task NewProjectAsync(Workbench workbench, bool newSolution)
    {
        if (workbench.TopLevel() is not Window owner)
        {
            return;
        }

        var existing = workbench.SolutionExplorer.Solution?.FilePath;
        var result = await NewProjectDialog.ShowAsync(owner, workbench, newSolution || existing is null, existing).ConfigureAwait(true);
        if (result?.CreatedSolution is { } solution)
        {
            await workbench.OpenSolutionAsync(solution).ConfigureAwait(true);
        }
    }

    public static IReadOnlyList<(string TitleKey, Func<Task> Run)> MenuFor(Workbench workbench, SolutionItemViewModel item)
    {
        var explorer = workbench.SolutionExplorer;
        return item.Kind switch
        {
            SolutionItemKind.Solution =>
            [
                ("Command.Solution.AddNewProject", () => NewProjectAsync(workbench, newSolution: false)),
                ("Command.Solution.AddExistingProject", explorer.AddExistingProjectAsync),
                ("-", NoOp),
                ("Command.Solution.Reload", () => explorer.RunAndReloadAsync(() => Task.CompletedTask)),
            ],
            SolutionItemKind.Project =>
            [
                ("Command.Build.Project", () => workbench.Build.BuildAsync(VsLike.Projects.Build.BuildKind.Build, item.Path)),
                ("Command.Run.SetStartup", () => SetStartup(workbench, item.Path)),
                ("Command.Tests.RunProject", () => RunProjectTests(workbench, item.Path)),
                ("-", NoOp),
                ("Command.Solution.AddClass", () => explorer.AddClassAsync(item)),
                ("Command.Solution.AddFolder", () => explorer.AddFolderAsync(item)),
                ("-", NoOp),
                ("Command.Solution.AddReference", () => EditReferencesAsync(workbench, item.Path)),
                ("Command.Solution.ManagePackages", () => OpenNuGet(workbench, item.Path)),
                ("Command.Solution.Properties", () => OpenProperties(workbench, item.Path)),
                ("-", NoOp),
                ("Command.Solution.RemoveProject", () => explorer.RemoveProjectAsync(item)),
            ],
            SolutionItemKind.Folder =>
            [
                ("Command.Solution.AddClass", () => explorer.AddClassAsync(item)),
                ("Command.Solution.AddFolder", () => explorer.AddFolderAsync(item)),
                ("-", NoOp),
                ("Command.Solution.Rename", () => explorer.RenameAsync(item)),
                ("Command.Solution.Delete", () => explorer.DeleteAsync(item)),
            ],
            SolutionItemKind.File =>
            [
                ("Command.Solution.Open", () => explorer.OpenAsync(item)),
                ("Command.Solution.Rename", () => explorer.RenameAsync(item)),
                ("Command.Solution.Delete", () => explorer.DeleteAsync(item)),
            ],
            SolutionItemKind.Dependencies when item.Project is { } project =>
            [
                ("Command.Solution.AddReference", () => EditReferencesAsync(workbench, project)),
                ("Command.Solution.ManagePackages", () => OpenNuGet(workbench, project)),
            ],
            SolutionItemKind.ProjectReference => [("Command.Solution.RemoveReference", () => explorer.RemoveReferenceAsync(item))],
            SolutionItemKind.Package when item.Project is { } project =>
            [
                ("Command.Solution.ManagePackages", () => OpenNuGet(workbench, project)),
                ("Command.Solution.RemovePackage", () => explorer.RemovePackageAsync(item)),
            ],
            _ => [],
        };
    }

    private static Task NoOp() => Task.CompletedTask;

    private static Task SetStartup(Workbench workbench, string project)
    {
        workbench.Build.SetStartupProject(project);
        if (workbench.Build.StartupProject?.Path != project)
        {
            workbench.Shell.StatusText = VsLike.Core.Localization.Loc.Get("Run.NotRunnable");
        }

        return Task.CompletedTask;
    }

    private static Task RunProjectTests(Workbench workbench, string project)
    {
        if (workbench.Tests.Roots.FirstOrDefault(r => r.ProjectPath == project) is not { } node)
        {
            workbench.Shell.StatusText = VsLike.Core.Localization.Loc.Get("Tests.NotTestProject");
            return Task.CompletedTask;
        }

        workbench.Shell.ShowPanel(ShellPanels.Tests.Id);
        return workbench.Tests.RunAsync(node);
    }

    private static async Task EditReferencesAsync(Workbench workbench, string project)
    {
        if (workbench.TopLevel() is not Window owner || workbench.SolutionExplorer.Solution is not { } solution)
        {
            return;
        }

        var candidates = solution.Projects.Select(p => p.FilePath).Where(p => p != project).ToList();
        var current = ProjectFile.Load(project).ProjectReferences();
        if (await ReferencesDialog.ShowAsync(owner, project, candidates, current).ConfigureAwait(true) is { } selected)
        {
            await workbench.SolutionExplorer.SetReferencesAsync(project, selected).ConfigureAwait(true);
        }
    }

    private static Task OpenNuGet(Workbench workbench, string project)
    {
        workbench.Documents.ShowDocument(new NuGetManagerViewModel(project, workbench.NuGet, workbench.ProjectService, workbench.SolutionExplorer.RunAndReloadAsync));
        return Task.CompletedTask;
    }

    private static Task OpenProperties(Workbench workbench, string project)
    {
        workbench.Documents.ShowDocument(new ProjectPropertiesViewModel(project));
        return Task.CompletedTask;
    }

    private static void Add(Workbench workbench, string id, string titleKey, string category, string? chord, Func<Task> execute, Func<bool>? canExecute = null)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = category,
            DefaultChord = chord is null ? null : KeyChord.Parse(chord),
            Execute = execute,
            CanExecute = canExecute,
        });
}
