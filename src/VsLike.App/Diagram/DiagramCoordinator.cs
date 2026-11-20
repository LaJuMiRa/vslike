using Avalonia.Threading;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Diagram.Code;
using VsLike.Diagram.Layout;
using VsLike.Roslyn;

namespace VsLike.App.Diagram;

/// <summary>Öffnet Klassendiagramme, legt neue an und hält offene Diagramme mit dem Code synchron.</summary>
public sealed class DiagramCoordinator
{
    private readonly Workbench _workbench;
    private readonly DiagramServices _services;

    public DiagramCoordinator(Workbench workbench)
    {
        _workbench = workbench;
        var workspace = workbench.Intelligence.Workspace;
        _services = new DiagramServices(
            () => (workspace as RoslynWorkspace)?.CurrentSolution,
            () => workspace.Projects,
            workbench.Intelligence.Features,
            workbench.Intelligence.Applier,
            workbench.Dialogs,
            workbench.Documents,
            path => workbench.Documents.OpenDocuments.FirstOrDefault(d => d.FilePath == path)?.TextDocument.Text,
            DeleteFileAsync);
        DiagramView.Dialogs = workbench.Dialogs;
        ViewLocator.Register<DiagramDocumentViewModel>(_ => new DiagramView());
        workbench.Documents.SpecialOpener = async path =>
        {
            if (!path.EndsWith(DiagramDocument.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await OpenAsync(path).ConfigureAwait(true);
            return true;
        };
        workspace.DiagnosticsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
    }

    public IEnumerable<DiagramDocumentViewModel> Open => _workbench.Shell.Documents.OfType<DiagramDocumentViewModel>()
        .Concat(_workbench.Shell.Factory.DocumentDock?.VisibleDockables?.OfType<DiagramDocumentViewModel>() ?? [])
        .Distinct();

    public async Task<DiagramDocumentViewModel> OpenAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var diagram = Open.FirstOrDefault(d => d.FilePath == fullPath) ?? new DiagramDocumentViewModel(fullPath, _services);
        _workbench.Documents.ShowDocument(diagram);
        await diagram.RefreshAsync().ConfigureAwait(true);
        return diagram;
    }

    /// <summary>Creates (or opens) <c>&lt;Name&gt;.cdiagram.json</c> in a folder showing all types declared below it.</summary>
    public async Task<DiagramDocumentViewModel?> ShowForFolderAsync(string folder, string name)
    {
        var path = Path.Combine(folder, name + DiagramDocument.Extension);
        if (!File.Exists(path))
        {
            if (_services.Solution() is not { } solution)
            {
                _workbench.Shell.StatusText = Loc.Get("Diagram.NoSolution");
                return null;
            }

            var model = await Task.Run(() => DiagramReader.ReadAsync(solution, CancellationToken.None)).ConfigureAwait(true);
            var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var types = model.Types.Where(t => !t.IsExternal && t.FileList.Any(f => f.StartsWith(prefix, StringComparison.Ordinal))).Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
            foreach (var relation in model.Relations.Where(r => types.Contains(r.From) && model.Find(r.To)?.IsExternal == true).ToList())
            {
                types.Add(relation.To);
            }

            var document = new DiagramDocument { Types = [.. types.Select(t => new TypePlacement(t, double.NaN, double.NaN))] };
            AutoLayout.Arrange(document, model, all: true).Save(path);
        }

        return await OpenAsync(path).ConfigureAwait(true);
    }

    private void RefreshAll()
    {
        foreach (var diagram in Open)
        {
            diagram.ScheduleRefresh();
        }
    }

    private async Task DeleteFileAsync(string path)
    {
        if (_workbench.Documents.OpenDocuments.FirstOrDefault(d => d.FilePath == path) is { } open)
        {
            open.TextDocument.UndoStack.MarkAsOriginalFile();
            await _workbench.Documents.CloseAsync(open).ConfigureAwait(true);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _workbench.Intelligence.Workspace.RemoveDocument(path);
    }
}
