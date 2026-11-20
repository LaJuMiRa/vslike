using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App.Dialogs;

public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _owner;

    public DialogService(Func<Window?> owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFileAsync(string titleKey)
    {
        if (_owner() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.Get(titleKey), AllowMultiple = false }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string titleKey)
    {
        if (_owner() is not { } window)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.Get(titleKey), AllowMultiple = false }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string titleKey, string suggestedName)
    {
        if (_owner() is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Loc.Get(titleKey), SuggestedFileName = suggestedName }).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string documentName)
    {
        if (_owner() is not { } window)
        {
            return SaveChangesChoice.Cancel;
        }

        return await ChoiceDialog.ShowAsync(
            window,
            Loc.Get("Dialog.SaveChanges.Title"),
            Loc.Format("Dialog.SaveChanges.Body", documentName),
            (Loc.Get("Dialog.DontSave"), SaveChangesChoice.Discard, false),
            (Loc.Get("Dialog.Cancel"), SaveChangesChoice.Cancel, false),
            (Loc.Get("Dialog.Save"), SaveChangesChoice.Save, true)).ConfigureAwait(true);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_owner() is not { } window)
        {
            return false;
        }

        return await ChoiceDialog.ShowAsync(window, title, message, (Loc.Get("Dialog.No"), false, false), (Loc.Get("Dialog.Yes"), true, true)).ConfigureAwait(true);
    }

    public async Task<string?> PromptAsync(string title, string message, string initialValue)
        => _owner() is { } window ? await PromptDialog.ShowAsync(window, title, message, initialValue).ConfigureAwait(true) : null;

    public async Task ShowMessageAsync(string title, string message)
    {
        if (_owner() is { } window)
        {
            await ChoiceDialog.ShowAsync(window, title, message, (Loc.Get("Dialog.Ok"), true, true)).ConfigureAwait(true);
        }
    }
}
