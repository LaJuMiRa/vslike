using Avalonia.Controls;
using VsLike.App.Analysis;
using VsLike.App.Build;
using VsLike.App.Diagram;
using VsLike.App.Editor;
using VsLike.App.Explorer;
using VsLike.App.Intelligence;
using VsLike.App.Output;
using VsLike.App.Problems;
using VsLike.App.References;
using VsLike.App.Search;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.App.SolutionExplorer;
using VsLike.App.Testing;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Files;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.Build;
using VsLike.Projects.Cli;
using VsLike.Projects.NuGet;
using VsLike.Projects.Testing;
using VsLike.Roslyn;

namespace VsLike.App;

/// <summary>Kompositionswurzel des Hauptfensters: verbindet Shell, Dokumente, Explorer und Suche.</summary>
public sealed class Workbench : IDisposable
{
    public Workbench(AppServices services, IDialogService dialogs, Func<TopLevel?> topLevel, IDotnetCli? cli = null)
    {
        Services = services;
        Dialogs = dialogs;
        TopLevel = topLevel;
        Documents = new DocumentManager(dialogs);
        Explorer = new ExplorerViewModel(Documents);
        Search = new FindInFilesViewModel(Explorer.AllFiles, () => Explorer.RootPath, Documents, Documents);
        Problems = new ProblemsViewModel(Documents);
        References = new ReferencesViewModel(Documents);
        Output = new OutputViewModel(Documents);
        Cli = cli ?? new DotnetCli();
        ProjectService = new ProjectService(Cli);
        NuGet = new NuGetClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        SolutionExplorer = new SolutionExplorerViewModel(this);
        Shell = new ShellViewModel(
            services,
            [
                (ShellPanels.Solution, SolutionExplorer),
                (ShellPanels.Explorer, Explorer),
                (ShellPanels.Problems, Problems),
                (ShellPanels.Output, Output),
                (ShellPanels.Search, Search),
                (ShellPanels.References, References),
                (ShellPanels.Properties, new EmptyPanelViewModel()),
                (ShellPanels.Tests, new EmptyPanelViewModel()),
            ]);
        Documents.Attach(Shell);
        var workspace = new RoslynWorkspace();
        Intelligence = new CodeIntelligence(workspace, new RoslynLanguageFeatures(workspace), Cli, Documents, Problems, Shell);
        StyleCop = new StyleCopController(Intelligence.Workspace, Cli, services, Shell, dialogs, ProjectService);
        Build = new BuildController(new BuildService(Cli), Output, Problems, Shell, Documents.SaveAllAsync);
        Tests = new TestExplorerViewModel(new TestService(Cli), Output, Shell, Intelligence.Features, Documents, Documents.SaveAllAsync, () => Build.Configuration);
        Shell.Factory.Panels.Single(p => p.Id == ShellPanels.Tests.Id).Content = Tests;
        Diagrams = new DiagramCoordinator(this);
        SolutionExplorer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SolutionExplorerViewModel.Solution) && SolutionExplorer.Solution is { } solution)
            {
                var projects = solution.Projects.Select(p => p.FilePath).ToList();
                Build.Load(solution.FilePath, projects);
                Tests.SetProjects(projects);
            }
        };
        Language = new LanguageActions(Intelligence, Documents, References, Shell, dialogs, () => Explorer.RootPath);
        Problems.CountsChanged += (_, _) => Shell.ProblemsText = Problems.ErrorCount + Problems.WarningCount == 0
            ? string.Empty
            : Loc.Format("Status.Problems", Problems.ErrorCount, Problems.WarningCount);
        Explorer.FilesChanged += async (_, paths) => await Documents.OnFilesChangedAsync(paths).ConfigureAwait(true);
        Documents.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentManager.Active))
            {
                TrackActiveDocument();
            }
        };
        Explorer.FilesChanged += (_, _) => SolutionExplorer.Refresh();
        Explorer.FilesChanged += (_, paths) => SyncClosedFiles(paths);
        WorkbenchCommands.Register(this);
        SolutionCommands.Register(this);
        BuildCommands.Register(this);
        LanguageCommands.Register(this);
    }

    /// <summary>Raised at the start of <see cref="Dispose"/>; the main window closes itself.</summary>
    public event EventHandler? Disposing;

    public AppServices Services { get; }

    public IDialogService Dialogs { get; }

    public Func<TopLevel?> TopLevel { get; }

    public ShellViewModel Shell { get; }

    public DocumentManager Documents { get; }

    public ExplorerViewModel Explorer { get; }

    public FindInFilesViewModel Search { get; }

    public ProblemsViewModel Problems { get; }

    public ReferencesViewModel References { get; }

    public LanguageActions Language { get; }

    public IDotnetCli Cli { get; }

    public ProjectService ProjectService { get; }

    public NuGetClient NuGet { get; }

    public SolutionExplorerViewModel SolutionExplorer { get; }

    public OutputViewModel Output { get; }

    public BuildController Build { get; }

    public StyleCopController StyleCop { get; }

    public DiagramCoordinator Diagrams { get; }

    public TestExplorerViewModel Tests { get; }

    public CodeIntelligence Intelligence { get; }

    /// <summary>Opens a .sln/.slnx/.csproj: shows its folder in the explorer and loads it into the workspace.</summary>
    public async Task<bool> OpenSolutionAsync(string path)
    {
        var fullPath = RealPath.Resolve(path);
        Explorer.OpenFolder(Path.GetDirectoryName(fullPath)!);
        Services.UpdateSettings(s => s.WithRecentSolution(fullPath));
        if (!fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            SolutionExplorer.Load(fullPath);
            Shell.ShowPanel(ShellPanels.Solution.Id);
        }
        else
        {
            Build.Load(fullPath, [fullPath]);
            Tests.SetProjects([fullPath]);
        }

        await StyleCop.ApplyForSolutionAsync(fullPath).ConfigureAwait(true);
        return await Intelligence.OpenSolutionAsync(fullPath).ConfigureAwait(true);
    }

    public void Dispose()
    {
        Disposing?.Invoke(this, EventArgs.Empty);
        Build.Stop();
        Tests.Cancel();
        Explorer.Dispose();
        Intelligence.Dispose();
    }

    /// <summary>External edits to C# files that are not open in an editor reach the workspace (analysis, diagrams).</summary>
    private void SyncClosedFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            if (Documents.OpenDocuments.Any(d => d.FilePath == path) || !Intelligence.Workspace.ContainsDocument(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    Intelligence.Workspace.UpdateDocumentText(path, File.ReadAllText(path));
                }
            }
            catch (IOException)
            {
                // The next change event retries.
            }
        }
    }

    private void TrackActiveDocument()
    {
        var document = Documents.Active;
        if (document is null)
        {
            Shell.CaretText = string.Empty;
            return;
        }

        void Update()
        {
            var (line, column) = document.Session?.Caret ?? (1, 1);
            Shell.CaretText = $"{Loc.Format("Status.LineCol", line, column)}  ·  {document.Language.Id}";
        }

        document.SessionChanged -= OnSessionChanged;
        document.SessionChanged += OnSessionChanged;
        Hook();
        Update();

        void OnSessionChanged(object? sender, EventArgs e) => Hook();

        void Hook()
        {
            if (document.Session is { } session)
            {
                session.CaretMoved -= OnCaret;
                session.CaretMoved += OnCaret;
            }
        }

        void OnCaret(object? sender, (int Line, int Column) caret)
        {
            if (ReferenceEquals(Documents.Active, document))
            {
                Update();
            }
        }
    }
}
