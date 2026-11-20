using Avalonia.Controls;
using VsLike.App.Editor;
using VsLike.App.Explorer;
using VsLike.App.Intelligence;
using VsLike.App.Problems;
using VsLike.App.Search;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Projects.Cli;
using VsLike.Roslyn;

namespace VsLike.App;

/// <summary>Kompositionswurzel des Hauptfensters: verbindet Shell, Dokumente, Explorer und Suche.</summary>
public sealed class Workbench : IDisposable
{
    public Workbench(AppServices services, IDialogService dialogs, Func<TopLevel?> topLevel, ICodeWorkspace? workspace = null, IDotnetCli? cli = null)
    {
        Services = services;
        Dialogs = dialogs;
        TopLevel = topLevel;
        Documents = new DocumentManager(dialogs);
        Explorer = new ExplorerViewModel(Documents);
        Search = new FindInFilesViewModel(Explorer.AllFiles, () => Explorer.RootPath, Documents, Documents);
        Problems = new ProblemsViewModel(Documents);
        Cli = cli ?? new DotnetCli();
        Shell = new ShellViewModel(
            services,
            [
                (ShellPanels.Explorer, Explorer),
                (ShellPanels.Problems, Problems),
                (ShellPanels.Output, new EmptyPanelViewModel()),
                (ShellPanels.Search, Search),
                (ShellPanels.Properties, new EmptyPanelViewModel()),
            ]);
        Documents.Attach(Shell);
        Intelligence = new CodeIntelligence(workspace ?? new RoslynWorkspace(), Cli, Documents, Problems, Shell);
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
        WorkbenchCommands.Register(this);
    }

    public AppServices Services { get; }

    public IDialogService Dialogs { get; }

    public Func<TopLevel?> TopLevel { get; }

    public ShellViewModel Shell { get; }

    public DocumentManager Documents { get; }

    public ExplorerViewModel Explorer { get; }

    public FindInFilesViewModel Search { get; }

    public ProblemsViewModel Problems { get; }

    public IDotnetCli Cli { get; }

    public CodeIntelligence Intelligence { get; }

    /// <summary>Opens a .sln/.slnx/.csproj: shows its folder in the explorer and loads it into the workspace.</summary>
    public async Task<bool> OpenSolutionAsync(string path)
    {
        Explorer.OpenFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Services.UpdateSettings(s => s.WithRecentSolution(Path.GetFullPath(path)));
        return await Intelligence.OpenSolutionAsync(path).ConfigureAwait(true);
    }

    public void Dispose()
    {
        Explorer.Dispose();
        Intelligence.Dispose();
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
