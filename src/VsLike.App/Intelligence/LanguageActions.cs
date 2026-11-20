using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit.Rendering;
using Microsoft.CodeAnalysis.CSharp;
using VsLike.App.Editor;
using VsLike.App.References;
using VsLike.App.Shell;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App.Intelligence;

/// <summary>Navigation, Rename, Formatierung und Code-Aktionen für den aktiven Editor.</summary>
public sealed class LanguageActions
{
    private readonly CodeIntelligence _intelligence;
    private readonly DocumentManager _documents;
    private readonly ReferencesViewModel _references;
    private readonly ShellViewModel _shell;
    private readonly IDialogService _dialogs;
    private readonly Func<string?> _root;

    public LanguageActions(CodeIntelligence intelligence, DocumentManager documents, ReferencesViewModel references, ShellViewModel shell, IDialogService dialogs, Func<string?> root)
    {
        _intelligence = intelligence;
        _documents = documents;
        _references = references;
        _shell = shell;
        _dialogs = dialogs;
        _root = root;
        intelligence.DefinitionRequested += async (_, e) => await GoToDefinitionAsync(e.Document, e.Offset).ConfigureAwait(true);
    }

    public bool CanRunOnActive => _documents.Active is { Session: not null, FilePath: not null } document && document.Language.Id == "csharp";

    private ILanguageFeatures Features => _intelligence.Features;

    public async Task GoToDefinitionAsync(EditorDocumentViewModel document, int offset)
    {
        if (_intelligence.BindingFor(document) is null || document.FilePath is not { } path)
        {
            return;
        }

        var locations = await Task.Run(() => Features.GoToDefinitionAsync(path, offset, CancellationToken.None)).ConfigureAwait(true);
        if (locations.Count == 0)
        {
            _shell.StatusText = Loc.Get("Status.NoDefinition");
            return;
        }

        if (locations.Count == 1)
        {
            await _documents.OpenAsync(locations[0].FilePath, locations[0].Line, locations[0].Column).ConfigureAwait(true);
            return;
        }

        ShowReferences(document, offset, locations);
    }

    public async Task FindReferencesAsync(EditorDocumentViewModel document, int offset)
    {
        if (_intelligence.BindingFor(document) is null || document.FilePath is not { } path)
        {
            return;
        }

        var locations = await Task.Run(() => Features.FindReferencesAsync(path, offset, CancellationToken.None)).ConfigureAwait(true);
        ShowReferences(document, offset, locations);
    }

    /// <returns>The number of changed files, or null if nothing was renamed.</returns>
    public async Task<int?> RenameAsync(EditorDocumentViewModel document, int offset)
    {
        if (_intelligence.BindingFor(document) is null || document.FilePath is not { } path)
        {
            return null;
        }

        var target = await Task.Run(() => Features.PrepareRenameAsync(path, offset, CancellationToken.None)).ConfigureAwait(true);
        if (target is null || !target.CanRename)
        {
            _shell.StatusText = Loc.Format("Rename.NotPossible", target?.Reason ?? target?.SymbolDisplay ?? "-");
            return null;
        }

        var newName = await _dialogs.PromptAsync(Loc.Format("Rename.Title", target.SymbolDisplay), Loc.Format("Rename.Message", target.ReferenceCount), target.CurrentName).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName) || newName == target.CurrentName)
        {
            return null;
        }

        if (!SyntaxFacts.IsValidIdentifier(newName))
        {
            await _dialogs.ShowMessageAsync(Loc.Get("Dialog.Error.Title"), Loc.Format("Rename.Invalid", newName)).ConfigureAwait(true);
            return null;
        }

        var edit = await Task.Run(() => Features.RenameAsync(path, offset, newName, CancellationToken.None)).ConfigureAwait(true);
        var changed = await _intelligence.Applier.ApplyAsync(edit).ConfigureAwait(true);
        _shell.StatusText = Loc.Format("Rename.Done", target.CurrentName, newName, changed);
        return changed;
    }

    public async Task FormatAsync(EditorDocumentViewModel document)
    {
        if (_intelligence.BindingFor(document) is null || document.FilePath is not { } path)
        {
            return;
        }

        var edit = await Task.Run(() => Features.FormatDocumentAsync(path, CancellationToken.None)).ConfigureAwait(true);
        await _intelligence.Applier.ApplyAsync(edit).ConfigureAwait(true);
        _shell.StatusText = Loc.Format("Status.Formatted", document.DisplayName);
    }

    public async Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(EditorDocumentViewModel document)
    {
        if (_intelligence.BindingFor(document) is null || document.FilePath is not { } path || document.Session is not { } session)
        {
            return [];
        }

        var area = session.Editor.TextArea;
        var start = area.Selection.IsEmpty ? session.Editor.CaretOffset : area.Selection.SurroundingSegment.Offset;
        var length = area.Selection.IsEmpty ? 0 : area.Selection.SurroundingSegment.Length;
        return await Task.Run(() => Features.GetCodeActionsAsync(path, start, length, CancellationToken.None)).ConfigureAwait(true);
    }

    public async Task ApplyCodeActionAsync(CodeActionInfo action)
    {
        var edit = await Task.Run(() => Features.GetCodeActionEditAsync(action, CancellationToken.None)).ConfigureAwait(true);
        await _intelligence.Applier.ApplyAsync(edit).ConfigureAwait(true);
    }

    /// <summary>Shows the quick-fix menu at the caret (⌘.).</summary>
    public async Task ShowCodeActionMenuAsync(EditorDocumentViewModel document)
    {
        var actions = await GetCodeActionsAsync(document).ConfigureAwait(true);
        if (document.Session is not { } session)
        {
            return;
        }

        if (actions.Count == 0)
        {
            _shell.StatusText = Loc.Get("Status.NoCodeActions");
            return;
        }

        var items = new List<Control>();
        foreach (var group in actions.OrderByDescending(a => a.IsFix).GroupBy(a => a.Group))
        {
            if (items.Count > 0)
            {
                items.Add(new Separator());
            }

            foreach (var action in group)
            {
                var title = group.Key is "Fix" or "Refactor" ? action.Title : $"{group.Key}: {action.Title}";
                var item = new MenuItem { Header = title };
                item.Click += async (_, _) => await ApplyCodeActionAsync(action).ConfigureAwait(true);
                items.Add(item);
            }
        }

        var view = session.Editor.TextArea.TextView;
        var caret = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(session.Editor.TextArea.Caret.Location), VisualYPosition.LineBottom) - view.ScrollOffset;
        var menu = new ContextMenu
        {
            ItemsSource = items,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            PlacementRect = new Rect(caret.X, caret.Y, 1, 1),
        };
        menu.Open(view);
    }

    private void ShowReferences(EditorDocumentViewModel document, int offset, IReadOnlyList<SourceLocation> locations)
    {
        var text = document.TextDocument;
        var (start, end) = VsLike.Editor.Editing.MultiCaretController.WordAt(text, Math.Min(offset, text.TextLength));
        _references.Show(text.GetText(start, end - start), locations, _root());
        _shell.ShowPanel(ShellPanels.References.Id);
    }
}
