using VsLike.Core.Commands;

namespace VsLike.App.Shell;

/// <summary>IntelliSense- und Navigationsbefehle.</summary>
public static class LanguageCommands
{
    public static void Register(Workbench workbench)
    {
        var docs = workbench.Documents;
        var language = workbench.Language;
        bool CanRun() => language.CanRunOnActive;
        int Caret() => docs.Active!.Session!.Editor.CaretOffset;

        Add(workbench, "edit.triggerCompletion", "Command.Edit.TriggerCompletion", "Category.Edit", "Ctrl+Space", () => workbench.Intelligence.BindingFor(docs.Active)?.TriggerCompletionAsync() ?? Task.CompletedTask, CanRun);
        Add(workbench, "edit.signatureHelp", "Command.Edit.SignatureHelp", "Category.Edit", "Cmd+Shift+Space", () => workbench.Intelligence.BindingFor(docs.Active)?.ShowSignatureHelpAsync() ?? Task.CompletedTask, CanRun);
        Add(workbench, "edit.quickFix", "Command.Edit.QuickFix", "Category.Edit", "Cmd+.", () => language.ShowCodeActionMenuAsync(docs.Active!), CanRun);
        Add(workbench, "edit.format", "Command.Edit.Format", "Category.Edit", "Shift+Alt+F", () => language.FormatAsync(docs.Active!), CanRun);
        Add(workbench, "edit.rename", "Command.Edit.Rename", "Category.Edit", "F2", () => language.RenameAsync(docs.Active!, Caret()), CanRun);
        Add(workbench, "nav.goToDefinition", "Command.Nav.GoToDefinition", "Category.Navigate", "F12", () => language.GoToDefinitionAsync(docs.Active!, Caret()), CanRun);
        Add(workbench, "nav.findReferences", "Command.Nav.FindReferences", "Category.Navigate", "Shift+F12", () => language.FindReferencesAsync(docs.Active!, Caret()), CanRun);

        var menus = workbench.Services.Menus;
        foreach (var id in new[] { "edit.triggerCompletion", "edit.signatureHelp", "edit.quickFix", "edit.format", "edit.rename" })
        {
            menus.Add("Menu.Edit", id, group: 4);
        }

        menus.Add("Menu.Edit", "nav.goToDefinition", group: 5);
        menus.Add("Menu.Edit", "nav.findReferences", group: 5);
    }

    private static void Add(Workbench workbench, string id, string titleKey, string category, string chord, Func<Task> execute, Func<bool> canExecute)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = category,
            DefaultChord = KeyChord.Parse(chord),
            Execute = execute,
            CanExecute = canExecute,
        });
}
