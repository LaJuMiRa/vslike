using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using VsLike.App.Shell;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App.Editor;

/// <summary>
/// Verwaltet die geöffneten Editor-Tabs: Öffnen, Neu, Speichern, Schließen mit Rückfrage und Neuladen nach externen Änderungen.
/// </summary>
public sealed partial class DocumentManager : ObservableObject, IDocumentNavigator
{
    private readonly IDialogService _dialogs;
    private readonly HashSet<EditorDocumentViewModel> _closingApproved = [];
    private ShellViewModel? _shell;
    private int _untitledCounter;

    [ObservableProperty]
    private EditorDocumentViewModel? _active;

    public DocumentManager(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public IEnumerable<EditorDocumentViewModel> OpenDocuments
        => _shell?.Factory.DocumentDock?.VisibleDockables?.OfType<EditorDocumentViewModel>() ?? [];

    private ShellViewModel Shell => _shell ?? throw new InvalidOperationException("DocumentManager is not attached.");

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
        shell.Factory.CanClose = CanCloseFromTab;
        shell.Factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is EditorDocumentViewModel document)
            {
                Active = document;
            }
            else if (e.Dockable is not null && e.Dockable.Owner == shell.Factory.DocumentDock)
            {
                Active = null;
            }
        };
        shell.Factory.DockableClosed += (_, e) =>
        {
            if (ReferenceEquals(e.Dockable, Active))
            {
                Active = OpenDocuments.FirstOrDefault();
            }
        };
    }

    public async Task OpenAsync(string path, int? line = null, int? column = null)
    {
        var fullPath = Path.GetFullPath(path);
        var document = OpenDocuments.FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.Ordinal));
        if (document is null)
        {
            string text;
            Encoding encoding;
            try
            {
                (text, encoding) = await TextFileIO.ReadAsync(fullPath).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowMessageAsync(Loc.Get("Dialog.Error.Title"), ex.Message).ConfigureAwait(true);
                return;
            }

            document = new EditorDocumentViewModel(fullPath, text, encoding);
            Add(document);
        }

        Activate(document);
        if (line is { } l)
        {
            if (document.Session is { } session)
            {
                session.GoTo(l, column ?? 1);
            }
            else
            {
                document.PendingNavigation = (l, column ?? 1);
            }
        }
    }

    public EditorDocumentViewModel New()
    {
        var document = new EditorDocumentViewModel(null, string.Empty, new UTF8Encoding(false), ++_untitledCounter);
        Add(document);
        Activate(document);
        return document;
    }

    public async Task<bool> SaveAsync(EditorDocumentViewModel document)
    {
        if (document.FilePath is null)
        {
            return await SaveAsAsync(document).ConfigureAwait(true);
        }

        return await WriteAsync(document, document.FilePath).ConfigureAwait(true);
    }

    public async Task<bool> SaveAsAsync(EditorDocumentViewModel document)
    {
        var path = await _dialogs.PickSaveFileAsync("Dialog.SaveAs", document.DisplayName).ConfigureAwait(true);
        return path is not null && await WriteAsync(document, path).ConfigureAwait(true);
    }

    public async Task SaveAllAsync()
    {
        foreach (var document in OpenDocuments.Where(d => d.IsDirty).ToList())
        {
            if (!await SaveAsync(document).ConfigureAwait(true))
            {
                return;
            }
        }
    }

    /// <summary>Closes a tab, asking to save unsaved changes. Returns false if the user cancelled.</summary>
    public async Task<bool> CloseAsync(EditorDocumentViewModel document)
    {
        if (!await ConfirmCloseAsync(document).ConfigureAwait(true))
        {
            return false;
        }

        _closingApproved.Add(document);
        try
        {
            Shell.Factory.CloseDockable(document);
        }
        finally
        {
            _closingApproved.Remove(document);
        }

        return true;
    }

    /// <summary>Asks for every dirty document; returns false when the user cancels (e.g. on quit).</summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var document in OpenDocuments.Where(d => d.IsDirty).ToList())
        {
            Activate(document);
            if (!await ConfirmCloseAsync(document).ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reloads unmodified open documents whose file changed on disk.</summary>
    public async Task OnFilesChangedAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            var document = OpenDocuments.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.Ordinal));
            if (document is null || !File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                (text, _) = await TextFileIO.ReadAsync(path).ConfigureAwait(true);
            }
            catch (IOException)
            {
                continue;
            }

            if (text == document.TextDocument.Text)
            {
                continue;
            }

            if (document.IsDirty)
            {
                Shell.StatusText = Loc.Format("Status.FileConflict", document.DisplayName);
            }
            else
            {
                document.ReloadText(text);
                Shell.StatusText = Loc.Format("Status.FileReloaded", document.DisplayName);
            }
        }
    }

    private async Task<bool> ConfirmCloseAsync(EditorDocumentViewModel document)
    {
        if (!document.IsDirty)
        {
            return true;
        }

        return await _dialogs.AskSaveChangesAsync(document.DisplayName).ConfigureAwait(true) switch
        {
            SaveChangesChoice.Save => await SaveAsync(document).ConfigureAwait(true),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private bool CanCloseFromTab(IDockable dockable)
    {
        if (dockable is not EditorDocumentViewModel document || !document.IsDirty || _closingApproved.Contains(document))
        {
            return true;
        }

        _ = CloseAsync(document);
        return false;
    }

    private async Task<bool> WriteAsync(EditorDocumentViewModel document, string path)
    {
        try
        {
            await TextFileIO.WriteAsync(path, document.TextDocument.Text, document.Encoding).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(Loc.Get("Dialog.Error.Title"), ex.Message).ConfigureAwait(true);
            return false;
        }

        document.MarkSaved(Path.GetFullPath(path));
        Shell.StatusText = Loc.Format("Status.Saved", document.DisplayName);
        return true;
    }

    private void Add(EditorDocumentViewModel document)
    {
        var dock = Shell.Factory.DocumentDock ?? throw new InvalidOperationException("No document dock.");
        Shell.Factory.AddDockable(dock, document);
    }

    private void Activate(EditorDocumentViewModel document)
    {
        Shell.Factory.SetActiveDockable(document);
        if (Shell.Factory.DocumentDock is { } dock)
        {
            Shell.Factory.SetFocusedDockable(dock, document);
        }

        Active = document;
    }
}
