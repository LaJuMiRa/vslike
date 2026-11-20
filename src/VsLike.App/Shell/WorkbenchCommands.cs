using VsLike.Core.Commands;

namespace VsLike.App.Shell;

/// <summary>Datei- und Bearbeiten-Befehle samt Menüeinträgen.</summary>
public static class WorkbenchCommands
{
    public static void Register(Workbench workbench)
    {
        var docs = workbench.Documents;
        var menus = workbench.Services.Menus;
        bool HasActive() => docs.Active is not null;

        Add(workbench, "file.new", "Command.File.New", "Category.File", "Cmd+N", () => docs.New());
        Add(workbench, "file.open", "Command.File.Open", "Category.File", "Cmd+O", async () =>
        {
            if (await workbench.Dialogs.PickFileAsync("Dialog.OpenFile").ConfigureAwait(true) is { } path)
            {
                await docs.OpenAsync(path).ConfigureAwait(true);
            }
        });
        Add(workbench, "file.openFolder", "Command.File.OpenFolder", "Category.File", "Cmd+Alt+O", async () =>
        {
            if (await workbench.Dialogs.PickFolderAsync("Dialog.OpenFolder").ConfigureAwait(true) is { } path)
            {
                workbench.Explorer.OpenFolder(path);
                workbench.Shell.ShowPanel(ShellPanels.Explorer.Id);
            }
        });
        Add(workbench, "file.openSolution", "Command.File.OpenSolution", "Category.File", "Cmd+Shift+O", async () =>
        {
            if (await workbench.Dialogs.PickFileAsync("Dialog.OpenSolution").ConfigureAwait(true) is { } path)
            {
                await workbench.OpenSolutionAsync(path).ConfigureAwait(true);
            }
        });
        Add(workbench, "file.save", "Command.File.Save", "Category.File", "Cmd+S", () => docs.SaveAsync(docs.Active!), HasActive);
        Add(workbench, "file.saveAs", "Command.File.SaveAs", "Category.File", "Cmd+Shift+S", () => docs.SaveAsAsync(docs.Active!), HasActive);
        Add(workbench, "file.saveAll", "Command.File.SaveAll", "Category.File", "Cmd+Alt+S", docs.SaveAllAsync);
        Add(workbench, "file.close", "Command.File.Close", "Category.File", "Cmd+W", () => docs.CloseAsync(docs.Active!), HasActive);
        menus.Add("Menu.File", "file.new");
        menus.Add("Menu.File", "file.open", group: 1);
        menus.Add("Menu.File", "file.openSolution", group: 1);
        menus.Add("Menu.File", "file.openFolder", group: 1);
        menus.Add("Menu.File", "file.save", group: 2);
        menus.Add("Menu.File", "file.saveAs", group: 2);
        menus.Add("Menu.File", "file.saveAll", group: 2);
        menus.Add("Menu.File", "file.close", group: 3);

        AddEdit(workbench, "edit.undo", "Command.Edit.Undo", "Cmd+Z", EditAction.Undo);
        AddEdit(workbench, "edit.redo", "Command.Edit.Redo", "Cmd+Shift+Z", EditAction.Redo);
        AddEdit(workbench, "edit.cut", "Command.Edit.Cut", "Cmd+X", EditAction.Cut);
        AddEdit(workbench, "edit.copy", "Command.Edit.Copy", "Cmd+C", EditAction.Copy);
        AddEdit(workbench, "edit.paste", "Command.Edit.Paste", "Cmd+V", EditAction.Paste);
        AddEdit(workbench, "edit.selectAll", "Command.Edit.SelectAll", "Cmd+A", EditAction.SelectAll);
        menus.Add("Menu.Edit", "edit.undo");
        menus.Add("Menu.Edit", "edit.redo");
        foreach (var id in new[] { "edit.cut", "edit.copy", "edit.paste", "edit.selectAll" })
        {
            menus.Add("Menu.Edit", id, group: 1);
        }

        bool HasSession() => docs.Active?.Session is not null;
        Add(workbench, "edit.find", "Command.Edit.Find", "Category.Edit", "Cmd+F", () => docs.Active!.Session!.OpenFind(), HasSession);
        Add(workbench, "edit.replace", "Command.Edit.Replace", "Category.Edit", "Cmd+Alt+F", () => docs.Active!.Session!.OpenReplace(), HasSession);
        Add(workbench, "edit.findInFiles", "Command.Edit.FindInFiles", "Category.Edit", "Cmd+Shift+F", () =>
        {
            workbench.Shell.ShowPanel(ShellPanels.Search.Id);
            workbench.Search.RequestFocus();
        });
        Add(workbench, "edit.toggleComment", "Command.Edit.ToggleComment", "Category.Edit", "Cmd+/", () => docs.Active!.Session!.ToggleComment(), HasSession);
        Add(workbench, "edit.addNextOccurrence", "Command.Edit.AddNextOccurrence", "Category.Edit", "Cmd+D", () => docs.Active!.Session!.MultiCaret.AddNextOccurrence(), HasSession);
        menus.Add("Menu.Edit", "edit.find", group: 2);
        menus.Add("Menu.Edit", "edit.replace", group: 2);
        menus.Add("Menu.Edit", "edit.findInFiles", group: 2);
        menus.Add("Menu.Edit", "edit.toggleComment", group: 3);
        menus.Add("Menu.Edit", "edit.addNextOccurrence", group: 3);
    }

    private static void Add(Workbench workbench, string id, string titleKey, string category, string? chord, Func<Task> execute, Func<bool>? canExecute = null)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = category,
            DefaultChord = chord is null ? null : KeyChord.Parse(chord),
            Execute = execute,
            CanExecute = canExecute,
        });

    private static void Add(Workbench workbench, string id, string titleKey, string category, string? chord, Action execute, Func<bool>? canExecute = null)
    {
        Task Run()
        {
            execute();
            return Task.CompletedTask;
        }

        Add(workbench, id, titleKey, category, chord, Run, canExecute);
    }

    private static void AddEdit(Workbench workbench, string id, string titleKey, string chord, EditAction action)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = "Category.Edit",
            DefaultChord = KeyChord.Parse(chord),
            BindGlobally = false,
            ShowInPalette = false,
            Execute = () =>
            {
                FocusedEdit.Run(workbench.TopLevel(), action);
                return Task.CompletedTask;
            },
        });
}
