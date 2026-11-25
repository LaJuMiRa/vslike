using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VsLike.Desktop.Diagram;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// The menus of the app (AppCommands in VsLikeApp.swift). Linux has no global menu bar: the same menus open from the
/// menu button in the title bar, and every shortcut is also a key binding of the main window. Shortcuts follow the macOS
/// ones with ⌘ → Ctrl (⌃⌘R → Ctrl+Alt+R).
/// </summary>
public static class AppCommands
{
    private static readonly KeyModifiers Ctrl = KeyModifiers.Control;
    private static readonly KeyModifiers Shift = KeyModifiers.Shift;
    private static readonly KeyModifiers Alt = KeyModifiers.Alt;

    /// <summary>The top-level menus (File, Edit, Code, Navigate, Build, Debug, View, VsLike).</summary>
    public static IEnumerable<MenuEntry?> Menus(Workspace workspace, MainWindow window) =>
        Specs(workspace, window).Select(menu => (MenuEntry?)new MenuEntry(menu.Title, Children: [.. menu.Items.Select(ToEntry)]));

    /// <summary>Key bindings for every menu command with a shortcut; disabled commands do nothing.</summary>
    public static IEnumerable<KeyBinding> KeyBindings(Workspace workspace, MainWindow window)
    {
        // The specs are rebuilt on every key press: titles and enabled states depend on the current workspace state.
        var gestures = Specs(workspace, window).SelectMany(m => m.Items).OfType<Command>().Where(c => c.Gesture is not null).Select(c => (c.Id, c.Gesture!)).ToList();
        foreach (var (id, gesture) in gestures)
        {
            yield return new KeyBinding
            {
                Gesture = gesture,
                Command = new RelayCommand(() =>
                {
                    var command = Specs(workspace, window).SelectMany(m => m.Items).OfType<Command>().FirstOrDefault(c => c.Id == id);
                    if (command is { Enabled: true })
                    {
                        command.Action();
                    }
                }),
            };
        }
    }

    private static List<Item?> RecentItems(Workspace w) =>
        [.. w.Preferences.RecentSolutions.Select(path => (Item?)new Command("recent:" + path, Path.GetFileName(path), () => _ = w.Open(path)))];

    private static List<Item?> ConfigurationItems(Workspace w) =>
        [.. BuildController.Configurations.Select(c => (Item?)new Command("config:" + c, c, () => w.Build.Configuration = c, null, true, w.Build.Configuration == c))];

    private static void FindInFiles(Workspace w, MainWindow window)
    {
        w.SidebarTab = SidebarTab.Search;
        window.MainView.ShowSidebar();
    }

    private static void StopAll(Workspace w)
    {
        _ = w.Debugger.Stop();
        w.Terminals.StopRuns();
        w.Build.Cancel();
    }

    private static MenuEntry? ToEntry(Item? item) => item switch
    {
        Command c => new MenuEntry(c.Title, c.Action, Gesture: c.Gesture?.ToString(), Enabled: c.Enabled, Checked: c.Checked),
        Submenu s => new MenuEntry(s.Title, Children: [.. s.Items.Select(ToEntry)], Enabled: s.Items.Count > 0),
        _ => null,
    };

    private static List<Menu> Specs(Workspace w, MainWindow window)
    {
        var editor = w.ActiveEditor;
        var diagram = (w.ActiveTab as EditorTab.Diagram)?.Editor;
        var document = w.ActiveDocument;
        var debugger = w.Debugger;
        return
        [
            new(
                L.T("File"),
                [
                    new Command("newSolution", L.T("New Solution…"), () => w.IsNewSolutionPresented = true, G(Key.N, Ctrl | Shift)),
                    new Command("newFile", L.T("New File…"), () => w.PromptNewFile(null), G(Key.N, Ctrl), w.IsOpen),
                    new Command("addProject", L.T("Add Project…"), () => w.NewProjectTarget = w.Solution, null, w.Solution is not null),
                    null,
                    new Command("open", L.T("Open…"), () => _ = OpenPanel.ChooseAndOpen(w, window), G(Key.O, Ctrl)),
                    new Submenu(L.T("Open Recent"), RecentItems(w)),
                    null,
                    new Command("save", L.T("Save"), () => _ = w.SaveActive(), G(Key.S, Ctrl), document is not null),
                    new Command("saveAll", L.T("Save All"), w.SaveAll, G(Key.S, Ctrl | Alt)),
                    null,
                    new Command("closeTab", L.T("Close Tab"), () => CloseTab(w, window), G(Key.W, Ctrl)),
                    new Command("closeSolution", L.T("Close Solution"), () => _ = w.CloseAll(), null, w.IsOpen),
                    null,
                    new Command("settings", L.T("Settings…"), () => SettingsWindow.Show(w, window), G(Key.OemComma, Ctrl)),
                    new Command("quit", L.T("Quit VsLike"), window.Close, G(Key.Q, Ctrl)),
                ]),
            new(
                L.T("Edit"),
                [
                    // Text fields and the editor handle Ctrl+Z themselves; the binding only reaches the window for diagrams.
                    new Command("undo", L.T("Undo"), () => _ = diagram?.Undo(), G(Key.Z, Ctrl), diagram is not null),
                    new Command("redo", L.T("Redo"), () => _ = diagram?.Redo(), G(Key.Z, Ctrl | Shift), diagram is not null),
                ]),
            new(
                L.T("Code"),
                [
                    new Command("quick", L.T("Quick Actions and Fixes"), () => editor?.ShowQuickActions(), G(Key.OemPeriod, Ctrl | Shift), editor is not null),
                    new Command("fixStyle", L.T("Fix All StyleCop Issues in File"), () => _ = document is null ? Task.CompletedTask : w.FixAllStyleCop(document), G(Key.OemPeriod, Ctrl | Alt | Shift), document is not null && w.StyleCopEnabled),
                    null,
                    new Command("completion", L.T("Show Completions"), () => editor?.TriggerCompletion(), G(Key.Space, Ctrl)),
                    new Command("signature", L.T("Parameter Info"), () => editor?.ShowSignatureHelp(), G(Key.Space, Ctrl | Shift)),
                    new Command("rename", L.T("Rename Symbol…"), () => editor?.RenameSymbol(), G(Key.F2, KeyModifiers.None)),
                    new Command("format", L.T("Format Document"), () => _ = document is null ? Task.CompletedTask : w.FormatDocument(document), G(Key.F, Alt | Shift)),
                    new Command("comment", L.T("Toggle Comment"), () => editor?.ToggleComment(), G(Key.OemQuestion, Ctrl)),
                    null,
                    new Command("stylecop", L.T("StyleCop Analysis"), () => _ = w.ToggleStyleCop(), null, w.Solution is not null, w.StyleCopEnabled),
                    new Command("stylecopSettings", L.T("StyleCop Settings…"), () => _ = w.OpenStyleCopSettings(), null, w.SolutionPath is not null),
                ]),
            new(
                L.T("Navigate"),
                [
                    new Command("quickOpen", L.T("Quick Open…"), () => w.IsQuickOpenPresented = true, G(Key.P, Ctrl)),
                    new Command("palette", L.T("Command Palette…"), () => w.IsCommandPalettePresented = true, G(Key.P, Ctrl | Shift)),
                    null,
                    new Command("definition", L.T("Go to Definition"), () => editor?.GoToDefinition(), G(Key.F12, KeyModifiers.None)),
                    new Command("references", L.T("Find References"), () => editor?.FindReferences(), G(Key.F12, Shift)),
                    new Command("line", L.T("Go to Line…"), () => editor?.PromptGoToLine(), G(Key.L, Ctrl)),
                    new Command("findInFiles", L.T("Find in Files"), () => FindInFiles(w, window), G(Key.F, Ctrl | Shift)),
                ]),
            new(
                L.T("Build"),
                [
                    new Command("build", L.T("Build Solution"), () => w.Build.Start(BuildKind.Build), G(Key.B, Ctrl)),
                    new Command("rebuild", L.T("Rebuild Solution"), () => w.Build.Start(BuildKind.Rebuild), G(Key.B, Ctrl | Shift)),
                    new Command("clean", L.T("Clean Solution"), () => w.Build.Start(BuildKind.Clean), G(Key.K, Ctrl | Shift)),
                    null,
                    new Command("runNoDebug", L.T("Run Without Debugging"), w.Build.StartWithoutDebugging, G(Key.R, Ctrl | Alt)),
                    new Command("tests", L.T("Run Tests"), () => w.Tests.Run(), G(Key.U, Ctrl)),
                    null,
                    new Submenu(L.T("Configuration"), ConfigurationItems(w)),
                ]),
            new(
                L.T("Debug"),
                [
                    new Command("debug", debugger.IsStopped ? L.T("Continue") : L.T("Start Debugging"), () => _ = debugger.Start(), G(Key.R, Ctrl)),
                    new Command("stop", L.T("Stop"), () => StopAll(w), G(Key.OemPeriod, Ctrl)),
                    new Command("pause", L.T("Pause"), () => _ = debugger.Pause(), null, debugger.IsActive && !debugger.IsStopped),
                    null,
                    new Command("stepOver", L.T("Step Over"), () => _ = debugger.StepOver(), G(Key.F10, KeyModifiers.None), debugger.IsStopped),
                    new Command("stepIn", L.T("Step Into"), () => _ = debugger.StepIn(), G(Key.F11, KeyModifiers.None), debugger.IsStopped),
                    new Command("stepOut", L.T("Step Out"), () => _ = debugger.StepOut(), G(Key.F11, Shift), debugger.IsStopped),
                    null,
                    new Command("breakpoint", L.T("Toggle Breakpoint"), () => editor?.ToggleBreakpointAtCaret(), G(Key.OemBackslash, Ctrl)),
                ]),
            new(
                L.T("View"),
                [
                    new Command("panel", w.IsBottomPanelVisible ? L.T("Hide Panel") : L.T("Show Panel"), () => w.IsBottomPanelVisible = !w.IsBottomPanelVisible, G(Key.J, Ctrl)),
                    new Command("problems", L.T("Problems"), () => w.Show(BottomTab.Problems), G(Key.M, Ctrl | Shift)),
                    new Command("terminal", L.T("Terminal"), () => _ = w.ShowTerminal(), G(Key.OemTilde, Ctrl)),
                    new Command("output", L.T("Output"), () => w.Show(BottomTab.Output), G(Key.O, Ctrl | Shift)),
                    new Command("diagram", L.T("Class Diagram"), () => _ = w.OpenSolutionDiagram(), G(Key.D, Ctrl | Shift), w.Solution is not null),
                    null,
                    new Command("larger", L.T("Larger Text"), () => w.Preferences.FontSize = Math.Min(28, w.Preferences.FontSize + 1), G(Key.OemPlus, Ctrl)),
                    new Command("smaller", L.T("Smaller Text"), () => w.Preferences.FontSize = Math.Max(9, w.Preferences.FontSize - 1), G(Key.OemMinus, Ctrl)),
                    new Command("actualSize", L.T("Actual Size"), () => w.Preferences.FontSize = 13, G(Key.D0, Ctrl)),
                ]),
        ];
    }

    private static void CloseTab(Workspace workspace, Window window)
    {
        if (workspace.ActiveTab is { } tab)
        {
            _ = workspace.Close(tab);
        }
        else
        {
            window.Close();
        }
    }

    private static KeyGesture G(Key key, KeyModifiers modifiers) => new(key, modifiers);

    private abstract record Item;

    private sealed record Command(string Id, string Title, Action Action, KeyGesture? Gesture = null, bool Enabled = true, bool? Checked = null) : Item;

    private sealed record Submenu(string Title, IReadOnlyList<Item?> Items) : Item;

    private sealed record Menu(string Title, IReadOnlyList<Item?> Items);

    private sealed class RelayCommand(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}

/// <summary>Open panels for solutions, projects, folders and files (OpenPanel in VsLikeApp.swift).</summary>
public static class OpenPanel
{
    private static readonly FilePickerFileType Openable = new(L.T("Solutions, projects and C# files"))
    {
        Patterns = ["*.sln", "*.slnx", "*.csproj", "*.cs", "*.cd"],
    };

    public static async Task ChooseAndOpen(Workspace workspace, TopLevel owner)
    {
        // GTK's portal dialog chooses files or folders, not both; files are the common case, a folder opens from the
        // welcome screen and "Open Folder…" in the palette.
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choose a solution, project, folder or C# file"),
            AllowMultiple = false,
            FileTypeFilter = [Openable, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await workspace.Open(path);
        }
    }

    public static async Task ChooseAndOpenFolder(Workspace workspace, TopLevel owner)
    {
        if (await ChooseFolder(owner, L.T("Choose a solution, project, folder or C# file")) is { } folder)
        {
            await workspace.Open(folder);
        }
    }

    public static async Task<string?> ChooseFolder(TopLevel owner, string message)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = message, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
