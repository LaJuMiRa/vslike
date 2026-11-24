using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// Everything about the open solution: tree, documents, engine, diagnostics and the build, test, debug and terminal
/// controllers. One per window. Lives on the UI thread.
/// </summary>
public sealed partial class Workspace : ObservableObject
{
    private readonly Dictionary<Guid, CancellationTokenSource> _syncTasks = [];
    private CancellationTokenSource? _notificationTask;
    private CancellationTokenSource? _treeRefresh;
    private FolderWatcher? _watcher;

    public Workspace(Preferences? preferences = null)
    {
        Preferences = preferences ?? Preferences.Shared;
        Build = new BuildController(this);
        Tests = new TestController(this);
        Debugger = new DebugController(this);
        Terminals = new TerminalController(this);
        Search = new SearchModel(this);
        Status = new StatusMessage(L.T("Ready"));
    }

    /// <summary>Gets or sets the factory for diagram tabs; the diagram part registers it at startup.</summary>
    public static IDiagramEditorFactory? DiagramFactory { get; set; }

    public Preferences Preferences { get; }

    public DiagnosticsStore Diagnostics { get; } = new();

    public OutputLog Output { get; } = new();

    public BuildController Build { get; }

    public TestController Tests { get; }

    public DebugController Debugger { get; }

    public TerminalController Terminals { get; }

    public SearchModel Search { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Title))]
    public partial string? RootPath { get; private set; }

    [ObservableProperty]
    public partial string? SolutionPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial SolutionModel? Solution { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<FileNode> Tree { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<FileNode> FolderTree { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<WorkspaceProject> Projects { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineReady))]
    public partial EngineStatus EngineStatus { get; private set; } = EngineStatus.Idle;

    [ObservableProperty]
    public partial SolutionSettings Settings { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTab), nameof(ActiveDocument), nameof(OpenDocuments))]
    public partial IReadOnlyList<EditorTab> Tabs { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTab), nameof(ActiveDocument))]
    public partial Guid? ActiveTabId { get; set; }

    [ObservableProperty]
    public partial SidebarTab SidebarTab { get; set; } = SidebarTab.Solution;

    [ObservableProperty]
    public partial BottomTab BottomTab { get; set; } = BottomTab.Problems;

    [ObservableProperty]
    public partial bool IsBottomPanelVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsInspectorVisible { get; set; }

    [ObservableProperty]
    public partial StatusMessage Status { get; set; }

    [ObservableProperty]
    public partial bool StyleCopEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsStyleCopLoading { get; set; }

    [ObservableProperty]
    public partial string? SelectedNodeId { get; set; }

    // Sheets and overlays.
    [ObservableProperty]
    public partial bool IsCommandPalettePresented { get; set; }

    [ObservableProperty]
    public partial bool IsQuickOpenPresented { get; set; }

    [ObservableProperty]
    public partial bool IsNewSolutionPresented { get; set; }

    [ObservableProperty]
    public partial SolutionModel? NewProjectTarget { get; set; }

    [ObservableProperty]
    public partial string? NugetProject { get; set; }

    [ObservableProperty]
    public partial string? PropertiesProject { get; set; }

    [ObservableProperty]
    public partial PromptRequest? Prompt { get; set; }

    [ObservableProperty]
    public partial AlertMessage? Alert { get; set; }

    [ObservableProperty]
    public partial ReferenceList? References { get; set; }

    public EngineClient? Engine { get; private set; }

    public List<Action<CodeDocument>> DocumentListeners { get; } = [];

    /// <summary>Gets or sets a replacement for the "save changes?" question (tests).</summary>
    public Func<IReadOnlyList<string>, SaveChoice>? ConfirmSaveHandler { get; set; }

    /// <summary>Gets or sets the modal questions of the main window.</summary>
    public IWorkspaceDialogs? Dialogs { get; set; }

    /// <summary>Gets or sets the editor that has (or last had) keyboard focus, for menu commands.</summary>
    public ICodeEditor? ActiveEditor { get; set; }

    public bool IsOpen => RootPath is not null;

    public string Title => Solution?.Name ?? (RootPath is null ? "VsLike" : Path.GetFileName(RootPath));

    public EditorTab? ActiveTab => Tabs.FirstOrDefault(t => t.Id == ActiveTabId);

    public CodeDocument? ActiveDocument => ActiveTab is EditorTab.Code code ? code.Document : null;

    public IReadOnlyList<CodeDocument> OpenDocuments => [.. Tabs.OfType<EditorTab.Code>().Select(t => t.Document)];

    public bool IsEngineReady => EngineStatus == EngineStatus.Ready;

    /// <summary>Opens a solution, project, folder or single file. A folder with exactly one solution opens that solution.</summary>
    public async Task Open(string rawPath)
    {
        var path = RealPath.Resolve(rawPath);
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            Alert = new AlertMessage(L.T("Not found"), path);
            return;
        }

        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!isDirectory && extension is not ("sln" or "slnx" or "csproj"))
        {
            if (RootPath is null)
            {
                await Open(Path.GetDirectoryName(path)!);
            }

            OpenFile(path);
            return;
        }

        if (!await CloseAll())
        {
            return;
        }

        var target = isDirectory ? SolutionInFolder(path) ?? path : path;
        var root = isDirectory && target == path ? path : Path.GetDirectoryName(target)!;
        RootPath = root;
        SolutionPath = target == path && isDirectory ? null : target;
        Solution = ReadSolution(SolutionPath);
        Settings = SolutionPath is null ? new SolutionSettings() : SolutionSettings.Load(SolutionPath);
        StyleCopEnabled = Settings.StyleCopEnabled ?? Preferences.StyleCopByDefault;
        Preferences.NoteRecent(SolutionPath ?? root);
        SidebarTab = Solution is null ? SidebarTab.Files : SidebarTab.Solution;
        RefreshTree();
        _watcher = new FolderWatcher(root, FilesChanged);
        Build.RestoreSelection();
        if (SolutionPath is not null)
        {
            await StartEngine(SolutionPath);
        }

        Tests.ReloadProjects();
    }

    /// <summary>Closes the solution after asking about unsaved changes. Returns false when the user cancelled.</summary>
    public async Task<bool> CloseAll()
    {
        var dirty = OpenDocuments.Where(d => d.IsDirty).ToList();
        if (dirty.Count > 0)
        {
            switch (await ConfirmSave([.. dirty.Select(d => d.Name)]))
            {
                case SaveChoice.Save:
                    dirty.ForEach(Save);
                    break;
                case SaveChoice.Cancel:
                    return false;
            }
        }

        await Debugger.Stop();
        Build.Cancel();
        Tabs = [];
        ActiveTabId = null;
        Diagnostics.Clear();
        _watcher?.Stop();
        _watcher = null;
        _notificationTask?.Cancel();
        if (Engine is { } engine)
        {
            Engine = null;
            EngineStatus = EngineStatus.Idle;
            await engine.Stop();
            engine.Dispose();
        }

        EngineStatus = EngineStatus.Idle;
        RootPath = null;
        SolutionPath = null;
        Solution = null;
        Projects = [];
        Tree = [];
        FolderTree = [];
        return true;
    }

    public async Task<SaveChoice> ConfirmSave(IReadOnlyList<string> names)
    {
        if (ConfirmSaveHandler is { } handler)
        {
            return handler(names);
        }

        return Dialogs is null ? SaveChoice.Cancel : await Dialogs.ConfirmSave(names);
    }

    public void RefreshTree()
    {
        if (RootPath is null)
        {
            return;
        }

        if (SolutionPath is not null && IsSolutionFile(SolutionPath))
        {
            try
            {
                Solution = SolutionReader.Read(SolutionPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Keep the last good model while the file is being written.
            }
        }

        FolderTree = FileTreeBuilder.Files(RootPath, 0);
        Tree = Solution is null ? FolderTree : FileTreeBuilder.SolutionTree(Solution);
    }

    /// <summary>Informs open diagrams that the code changed.</summary>
    public void NotifyCodeChanged()
    {
        foreach (var diagram in Tabs.OfType<EditorTab.Diagram>())
        {
            diagram.Editor.ScheduleRefresh();
        }
    }

    /// <summary>The project whose folder contains a file (deepest match).</summary>
    public SolutionProject? ProjectContaining(string path) =>
        Solution?.Projects.Where(p => path.StartsWith(p.Directory + "/", StringComparison.Ordinal)).MaxBy(p => p.Directory.Length);

    internal static bool IsSolutionFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";

    private static string? SolutionInFolder(string folder)
    {
        var entries = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        var solutions = entries.Where(e => e.EndsWith(".slnx", StringComparison.Ordinal) || e.EndsWith(".sln", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (solutions.Count == 1)
        {
            return Path.Combine(folder, solutions[0]);
        }

        var project = entries.FirstOrDefault(e => e.EndsWith(".csproj", StringComparison.Ordinal));
        return project is not null && solutions.Count == 0 ? Path.Combine(folder, project) : null;
    }

    private static SolutionModel? ReadSolution(string? solutionPath)
    {
        if (solutionPath is null)
        {
            return null;
        }

        if (IsSolutionFile(solutionPath))
        {
            try
            {
                return SolutionReader.Read(solutionPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                return null;
            }
        }

        var project = new SolutionProject(Path.GetFileNameWithoutExtension(solutionPath), solutionPath, "/");
        return new SolutionModel(solutionPath, SolutionFormat.Slnx, [project], []);
    }

    private void FilesChanged(IReadOnlyList<string> paths)
    {
        var relevant = paths.Where(p => !p.Split('/').Any(FileTreeBuilder.Ignored.Contains)).ToList();
        if (relevant.Count == 0)
        {
            return;
        }

        _treeRefresh?.Cancel();
        var refresh = new CancellationTokenSource();
        _treeRefresh = refresh;
        _ = Debounced(TimeSpan.FromMilliseconds(250), refresh.Token, () =>
        {
            RefreshTree();
            foreach (var path in relevant.Distinct())
            {
                SyncFromDisk(path);
            }

            if (relevant.Any(p => p.EndsWith(".csproj", StringComparison.Ordinal) || IsSolutionFile(p)))
            {
                Tests.ReloadProjects();
            }
        });
    }

    /// <summary>Keeps open documents and the engine in step with external changes.</summary>
    private void SyncFromDisk(string path)
    {
        var exists = File.Exists(path);
        if (OpenDocuments.FirstOrDefault(d => d.Path == path) is { } document)
        {
            if (exists && !document.IsDirty && TryReadText(path) is { } disk)
            {
                document.Reload(disk.Replace("\r\n", "\n", StringComparison.Ordinal));
            }

            return;
        }

        if (!path.EndsWith(".cs", StringComparison.Ordinal) || Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        if (exists && TryReadText(path) is { } text)
        {
            _ = Run(async () =>
            {
                if (!await engine.AddDocument(path, text))
                {
                    engine.UpdateDocument(path, text);
                }

                NotifyCodeChanged();
            });
        }
        else if (!exists)
        {
            _ = Run(async () =>
            {
                await engine.RemoveDocument(path);
                NotifyCodeChanged();
            });
        }
    }

    /// <summary>Runs <paramref name="action"/> after <paramref name="delay"/> unless cancelled (debounce), on the calling context.</summary>
    internal static async Task Debounced(TimeSpan delay, CancellationToken token, Action action)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            action();
        }
    }

    /// <summary>Runs fire-and-forget work, ignoring engine errors like <c>try?</c> in the Swift original.</summary>
    internal static async Task Run(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is EngineException or IOException or OperationCanceledException or CommandException)
        {
            // Best effort, as in the original.
        }
    }

    internal static string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
