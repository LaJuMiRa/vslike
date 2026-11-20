using VsLike.Core.Commands;

namespace VsLike.App.Shell;

/// <summary>Befehle des Debuggen-Menüs.</summary>
public static class DebugCommands
{
    public static void Register(Workbench workbench)
    {
        var debug = workbench.Debugger;
        Add(workbench, "debug.start", "Command.Debug.Start", "F5", debug.StartOrContinueAsync, () => debug.CanStart);
        Add(workbench, "debug.stop", "Command.Debug.Stop", "Shift+F5", debug.StopAsync, () => debug.IsDebugging);
        Add(workbench, "debug.pause", "Command.Debug.Pause", null, debug.PauseAsync, () => debug.IsDebugging && !debug.IsStopped);
        Add(workbench, "debug.stepOver", "Command.Debug.StepOver", "F10", debug.StepOverAsync, () => debug.CanStep);
        Add(workbench, "debug.stepInto", "Command.Debug.StepInto", "F11", debug.StepIntoAsync, () => debug.CanStep);
        Add(workbench, "debug.stepOut", "Command.Debug.StepOut", "Shift+F11", debug.StepOutAsync, () => debug.CanStep);
        Add(workbench, "debug.toggleBreakpoint", "Command.Debug.ToggleBreakpoint", "F9", () =>
        {
            if (workbench.Documents.Active is { FilePath: { } path, Session: { } session })
            {
                debug.ToggleBreakpoint(path, session.Caret.Line);
            }

            return Task.CompletedTask;
        });
        var menus = workbench.Services.Menus;
        menus.Add("Menu.Debug", "debug.start");
        menus.Add("Menu.Debug", "debug.stop");
        menus.Add("Menu.Debug", "debug.pause");
        menus.Add("Menu.Debug", "debug.stepOver", group: 1);
        menus.Add("Menu.Debug", "debug.stepInto", group: 1);
        menus.Add("Menu.Debug", "debug.stepOut", group: 1);
        menus.Add("Menu.Debug", "debug.toggleBreakpoint", group: 2);
    }

    private static void Add(Workbench workbench, string id, string titleKey, string? chord, Func<Task> execute, Func<bool>? canExecute = null)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = "Category.Debug",
            DefaultChord = chord is null ? null : KeyChord.Parse(chord),
            Execute = execute,
            CanExecute = canExecute,
        });
}
