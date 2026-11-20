using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.Core.Files;
using VsLike.Diagram.Model;
using VsLike.Projects;
using VsLike.Projects.Build;
using VsLike.Projects.Solutions;

namespace VsLike.App.Tests;

/// <summary>
/// Abschluss-Test: neue Solution anlegen → Klasse im Diagramm erstellen → IntelliSense nutzen → StyleCop an/aus →
/// bauen → debuggen. Läuft gegen das echte .NET SDK und netcoredbg.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly string _root = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-e2e", Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task NewSolution_Diagram_IntelliSense_StyleCop_Build_Debug()
    {
        using var workbench = new Workbench(new AppServices(_platform), new FakeDialogService(), () => null);
        var window = new MainWindow(workbench);
        window.Show();

        // 1. Neue Solution mit Konsolenprojekt.
        var solution = await workbench.ProjectService.CreateSolutionAsync(_root, "Hello", SolutionFormat.Slnx, CancellationToken.None);
        var project = await workbench.ProjectService.CreateProjectAsync("console", "Hello.App", _root, "net10.0", CancellationToken.None);
        await workbench.ProjectService.AddProjectToSolutionAsync(solution, project, null, CancellationToken.None);
        Assert.True(await workbench.OpenSolutionAsync(solution), workbench.Shell.StatusText);
        Assert.Equal("Hello.App", workbench.Build.StartupProject?.Name);

        // 2. Klasse im Klassendiagramm anlegen und eine Property ergänzen.
        var folder = Path.GetDirectoryName(project)!;
        var diagram = await workbench.Diagrams.ShowForFolderAsync(folder, "Hello.App");
        Assert.NotNull(diagram);
        Assert.True(await diagram.CreateTypeAsync(DiagramTypeKind.Class, "Greeter", 40, 40));
        Assert.True(MemberSpec.TryParse(MemberKind.Property, "+ Name: string = \"World\"", "Greeter", out var name));
        await diagram.AddMemberAsync("Hello.App.Greeter", name);
        Assert.Contains("public string Name { get; set; } = \"World\";", File.ReadAllText(Path.Combine(folder, "Greeter.cs")), StringComparison.Ordinal);

        // 3. IntelliSense: Program.cs nutzt die neue Klasse, Completion nach "greeter." kennt Name.
        var programPath = Path.Combine(folder, "Program.cs");
        await workbench.Documents.OpenAsync(programPath);
        var program = workbench.Documents.Active!;
        await UiWait.UntilAsync(() => program.Session is not null, TimeSpan.FromSeconds(5), "editor session");
        program.TextDocument.Text = "using Hello.App;\n\nvar greeter = new Greeter();\nConsole.WriteLine($\"Hello {greeter.Name}\");\n";
        var dot = program.TextDocument.Text.IndexOf("greeter.Name", StringComparison.Ordinal) + "greeter.".Length;
        workbench.Intelligence.Workspace.UpdateDocumentText(programPath, program.TextDocument.Text);
        var completion = await workbench.Intelligence.Features.GetCompletionAsync(programPath, dot, CancellationToken.None);
        Assert.Contains(completion!.Items, i => i.DisplayText == "Name");

        // 4. StyleCop an (Warnungen erscheinen) und wieder aus (verschwinden).
        await workbench.StyleCop.ToggleAsync();
        await UiWait.UntilAsync(() => workbench.Problems.All.Any(d => d.Category == "StyleCop"), TimeSpan.FromSeconds(20), "StyleCop warnings");
        await workbench.StyleCop.ToggleAsync();
        await UiWait.UntilAsync(() => !workbench.Problems.All.Any(d => d.Category == "StyleCop"), TimeSpan.FromSeconds(20), "StyleCop off");

        // 5. Bauen.
        var build = await workbench.Build.BuildAsync(BuildKind.Build);
        Assert.True(build?.Succeeded, string.Join('\n', workbench.Output.Lines.Select(l => l.Text)));

        // 6. Debuggen: Haltepunkt in Program.cs, Variable sichtbar, Programm läuft zu Ende.
        workbench.Debugger.ToggleBreakpoint(programPath, 4);
        await workbench.Debugger.StartOrContinueAsync();
        await UiWait.UntilAsync(() => workbench.Debugger.IsStopped, TimeSpan.FromSeconds(60), "breakpoint: " + workbench.Shell.StatusText);
        Assert.Equal(4, workbench.Debugger.SelectedFrame?.Line);
        Assert.Contains(workbench.Debugger.Locals.SelectMany(s => s.Children), v => v.Name == "greeter");
        workbench.Debugger.ToggleBreakpoint(programPath, 4);
        await workbench.Debugger.StartOrContinueAsync();
        await UiWait.UntilAsync(() => !workbench.Debugger.IsDebugging, TimeSpan.FromSeconds(30), "program exit");
        Assert.Contains(workbench.Debugger.Console, l => l == "Hello World");
    }

    public void Dispose()
    {
        _platform.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
