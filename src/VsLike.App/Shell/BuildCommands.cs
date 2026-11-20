using VsLike.Core.Commands;
using VsLike.Projects.Build;

namespace VsLike.App.Shell;

/// <summary>Befehle des Build-Menüs: Build, Start ohne Debugger, Stopp und Tests.</summary>
public static class BuildCommands
{
    public static void Register(Workbench workbench)
    {
        var build = workbench.Build;
        var tests = workbench.Tests;
        bool CanBuild() => build.CanBuild;
        Add(workbench, "build.solution", "Command.Build.Solution", "Cmd+B", () => build.BuildAsync(BuildKind.Build), CanBuild);
        Add(workbench, "build.rebuild", "Command.Build.Rebuild", "Cmd+Shift+B", () => build.BuildAsync(BuildKind.Rebuild), CanBuild);
        Add(workbench, "build.clean", "Command.Build.Clean", null, () => build.BuildAsync(BuildKind.Clean), CanBuild);
        Add(workbench, "build.toggleConfiguration", "Command.Build.ToggleConfiguration", null, () =>
        {
            build.Configuration = build.Configuration == "Debug" ? "Release" : "Debug";
            return Task.CompletedTask;
        });
        Add(workbench, "run.start", "Command.Run.Start", "Cmd+R", build.RunAsync, () => build.CanRun);
        Add(workbench, "run.stop", "Command.Run.Stop", "Cmd+Shift+.", () =>
        {
            build.Stop();
            tests.Cancel();
            return Task.CompletedTask;
        });
        Add(workbench, "tests.discover", "Command.Tests.Discover", null, tests.DiscoverAsync, () => tests.HasTestProjects);
        Add(workbench, "tests.runAll", "Command.Tests.RunAll", "Cmd+Alt+R", () => tests.RunAsync(), () => tests.HasTestProjects);
        Add(workbench, "tests.runFailed", "Command.Tests.RunFailed", null, tests.RunFailedAsync, () => tests.HasTestProjects);

        var menus = workbench.Services.Menus;
        foreach (var id in new[] { "build.solution", "build.rebuild", "build.clean", "build.toggleConfiguration" })
        {
            menus.Add("Menu.Build", id);
        }

        menus.Add("Menu.Build", "run.start", group: 1);
        menus.Add("Menu.Build", "run.stop", group: 1);
        foreach (var id in new[] { "tests.discover", "tests.runAll", "tests.runFailed" })
        {
            menus.Add("Menu.Build", id, group: 2);
        }
    }

    private static void Add(Workbench workbench, string id, string titleKey, string? chord, Func<Task> execute, Func<bool>? canExecute = null)
        => workbench.Services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = "Category.Build",
            DefaultChord = chord is null ? null : KeyChord.Parse(chord),
            Execute = execute,
            CanExecute = canExecute,
        });
}
