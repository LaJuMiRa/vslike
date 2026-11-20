using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.App.SolutionExplorer;
using VsLike.Projects.ProjectFiles;

namespace VsLike.App.Tests;

public sealed class SolutionExplorerTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();
    private readonly FakeDialogService _dialogs = new();

    [AvaloniaFact]
    public async Task Tree_ShowsProjectsDependenciesAndFiles()
    {
        using var workbench = await OpenAsync();

        var root = Assert.Single(workbench.SolutionExplorer.Roots);
        Assert.Equal(SolutionItemKind.Solution, root.Kind);
        Assert.Equal(["App", "Lib1", "Lib2"], root.Children.Where(c => c.Kind == SolutionItemKind.Project).Select(c => c.Name));
        var lib1 = root.Children.Single(c => c.Name == "Lib1");
        var dependencies = lib1.Children.Single(c => c.Kind == SolutionItemKind.Dependencies);
        Assert.Equal(["Lib2"], dependencies.Children.Select(c => c.Name));
        Assert.Contains(lib1.Children, c => c.Kind == SolutionItemKind.File && c.Name == "Car.cs");
        Assert.DoesNotContain(lib1.SelfAndDescendants(), n => n.Name is "obj" or "bin");
    }

    [AvaloniaFact]
    public async Task Refresh_AfterSolutionFileDeleted_ClearsTreeInsteadOfThrowing()
    {
        using var workbench = await OpenAsync();
        File.Delete(_solution.SolutionPath);

        workbench.SolutionExplorer.Refresh();

        Assert.Empty(workbench.SolutionExplorer.Roots);
        Assert.False(workbench.SolutionExplorer.HasSolution);
    }

    [AvaloniaFact]
    public async Task AddClass_CreatesFileWithFolderNamespaceAndKnownToWorkspace()
    {
        using var workbench = await OpenAsync();
        var app = Node(workbench, SolutionItemKind.Project, "App");
        Directory.CreateDirectory(_solution.PathOf("App/Models"));
        workbench.SolutionExplorer.Refresh();
        var models = Node(workbench, SolutionItemKind.Folder, "Models");
        _dialogs.NextPrompt = "Order";

        var path = await workbench.SolutionExplorer.AddClassAsync(models);

        Assert.Equal(_solution.PathOf("App/Models/Order.cs"), path);
        Assert.Equal("namespace Sample.App.Models;\n\npublic class Order\n{\n}\n", await File.ReadAllTextAsync(path!));
        Assert.True(workbench.Intelligence.Workspace.ContainsDocument(path!));
        Assert.Equal(path, workbench.Documents.Active?.FilePath);
        Assert.NotNull(app);
    }

    [AvaloniaFact]
    public async Task MoveAndRename_UpdateDiskAndWorkspace()
    {
        using var workbench = await OpenAsync();
        Directory.CreateDirectory(_solution.PathOf("Lib1/Parts"));
        workbench.SolutionExplorer.Refresh();
        var car = Node(workbench, SolutionItemKind.File, "Car.cs");
        var parts = Node(workbench, SolutionItemKind.Folder, "Parts");

        Assert.True(await workbench.SolutionExplorer.MoveAsync(car, parts));
        var moved = _solution.PathOf("Lib1/Parts/Car.cs");
        Assert.True(File.Exists(moved));
        Assert.True(workbench.Intelligence.Workspace.ContainsDocument(moved));

        _dialogs.NextPrompt = "Vehicle.cs";
        await workbench.SolutionExplorer.RenameAsync(Node(workbench, SolutionItemKind.File, "Car.cs"));
        Assert.True(File.Exists(_solution.PathOf("Lib1/Parts/Vehicle.cs")));
        Assert.False(workbench.Intelligence.Workspace.ContainsDocument(moved));
    }

    [AvaloniaFact]
    public async Task SetReferences_AddsAndRemovesProjectReferences()
    {
        using var workbench = await OpenAsync();
        var app = _solution.PathOf("App/App.csproj");
        var lib2 = _solution.PathOf("Lib2/Lib2.csproj");

        Assert.True(await workbench.SolutionExplorer.RunAndReloadAsync(() => workbench.SolutionExplorer.SetReferencesAsync(app, [lib2])));

        Assert.Equal([lib2], ProjectFile.Load(app).ProjectReferences());
        Assert.Contains(workbench.Intelligence.Workspace.Projects.Single(p => p.Name == "App").ProjectReferences, r => r == lib2);
    }

    public void Dispose()
    {
        _platform.Dispose();
        _solution.Dispose();
    }

    private static SolutionItemViewModel Node(Workbench workbench, SolutionItemKind kind, string name)
        => workbench.SolutionExplorer.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Kind == kind && n.Name == name);

    private async Task<Workbench> OpenAsync()
    {
        var workbench = new Workbench(new AppServices(_platform), _dialogs, () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        return workbench;
    }
}
