namespace VsLike.Core.Documents;

public interface IDialogService
{
    Task<string?> PickFileAsync(string titleKey);

    Task<string?> PickFolderAsync(string titleKey);

    Task<string?> PickSaveFileAsync(string titleKey, string suggestedName);

    Task<SaveChangesChoice> AskSaveChangesAsync(string documentName);

    /// <summary>Shows a yes/no confirmation; returns true for yes.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Asks for a line of text; returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialValue);
}
