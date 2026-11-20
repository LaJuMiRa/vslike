using VsLike.Core.Documents;

namespace VsLike.App.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public string? NextSavePath { get; set; }

    public SaveChangesChoice NextSaveChoice { get; set; } = SaveChangesChoice.Cancel;

    public int SaveChangesPrompts { get; private set; }

    public Task<string?> PickFileAsync(string titleKey) => Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string titleKey) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(string titleKey, string suggestedName) => Task.FromResult(NextSavePath);

    public Task<SaveChangesChoice> AskSaveChangesAsync(string documentName)
    {
        SaveChangesPrompts++;
        return Task.FromResult(NextSaveChoice);
    }

    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
}
