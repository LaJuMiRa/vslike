using Avalonia.Controls;
using VsLike.App.Editor;
using VsLike.App.Explorer;
using VsLike.App.Search;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App;

/// <summary>Kompositionswurzel des Hauptfensters: verbindet Shell, Dokumente, Explorer und Suche.</summary>
public sealed class Workbench : IDisposable
{
    public Workbench(AppServices services, IDialogService dialogs, Func<TopLevel?> topLevel)
    {
        Services = services;
        Dialogs = dialogs;
        TopLevel = topLevel;
        Documents = new DocumentManager(dialogs);
        Explorer = new ExplorerViewModel(Documents);
        Search = new FindInFilesViewModel(Explorer.AllFiles, () => Explorer.RootPath, Documents, Documents);
        Shell = new ShellViewModel(
            services,
            [
                (ShellPanels.Explorer, Explorer),
                (ShellPanels.Problems, new EmptyPanelViewModel()),
                (ShellPanels.Output, new EmptyPanelViewModel()),
                (ShellPanels.Search, Search),
                (ShellPanels.Properties, new EmptyPanelViewModel()),
            ]);
        Documents.Attach(Shell);
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

    public void Dispose() => Explorer.Dispose();

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
