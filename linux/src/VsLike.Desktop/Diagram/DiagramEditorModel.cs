using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// A class diagram tab (port of DiagramEditorModel.swift). Types and members always come from the code (read by the
/// engine); every diagram action rewrites only the affected declaration and is one undo step for code and layout
/// together. Positions live in <c>&lt;Name&gt;.cd</c> (older <c>&lt;Name&gt;.cdiagram.json</c> files still open).
/// </summary>
public sealed partial class DiagramEditorModel : ObservableObject, IDiagramEditor
{
    private readonly Workspace _workspace;
    private readonly List<UndoEntry> _undoStack = [];
    private readonly List<UndoEntry> _redoStack = [];
    private CancellationTokenSource? _refreshDelay;
    private CancellationTokenSource? _viewSaveDelay;
    private DiagramModel _model = DiagramModel.Empty;
    private DiagramDocument _layout;

    public DiagramEditorModel(string path, Workspace workspace)
    {
        Path = path;
        _workspace = workspace;
        _layout = DiagramDocument.Load(path);
        UpdateGeometry();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Path { get; }

    public Workspace Workspace => _workspace;

    public DiagramModel Model
    {
        get
        {
            return _model;
        }

        private set
        {
            if (SetProperty(ref _model, value))
            {
                UpdateGeometry();
                OnPropertyChanged(nameof(SelectedType));
                OnPropertyChanged(nameof(MissingTypes));
            }
        }
    }

    public DiagramDocument Layout
    {
        get
        {
            return _layout;
        }

        private set
        {
            if (!ReferenceEquals(_layout, value))
            {
                _layout = value;
                OnPropertyChanged();
                UpdateGeometry();
                OnPropertyChanged(nameof(MissingTypes));
            }
        }
    }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    [ObservableProperty]
    public partial bool CanRedo { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedType))]
    public partial string? Selection { get; set; }

    [ObservableProperty]
    public partial DiagramTool Tool { get; set; } = DiagramTool.Select;

    [ObservableProperty]
    public partial bool ShowsExternal { get; set; } = true;

    /// <summary>Gets the boxes, computed once per change of code, layout or filter (not on every redraw).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiagramBox> Boxes { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<DiagramEdge> Edges { get; private set; } = [];

    public string Name => DiagramDocument.Name(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "/";

    /// <summary>
    /// Gets where new types go: the diagram's folder, or the startup project's folder (where Program.cs is) when the
    /// diagram lies outside every project, such as an older diagram next to the solution. Paths are compared resolved.
    /// </summary>
    public string NewTypeFolder
    {
        get
        {
            var diagramFolder = RealPath.Resolve(Folder);
            var projects = _workspace.Solution?.Projects ?? [];
            if (projects.Any(project => IsInside(diagramFolder, RealPath.Resolve(project.Directory))))
            {
                return diagramFolder;
            }

            return _workspace.Build.StartupProject is { } startup
                ? RealPath.Resolve(System.IO.Path.GetDirectoryName(startup) ?? diagramFolder)
                : diagramFolder;
        }
    }

    private static bool IsInside(string folder, string directory) =>
        folder == directory || folder.StartsWith(directory + "/", StringComparison.Ordinal);

    public DiagramType? SelectedType => Selection is null ? null : Model.Find(Selection);

    /// <summary>Gets source types that are not on the diagram yet (for "Add existing type").</summary>
    public IReadOnlyList<DiagramType> MissingTypes
    {
        get
        {
            var shown = Layout.Types.Select(t => t.FullName).ToHashSet();
            return [.. Model.Types.Where(t => !t.IsExternal && !shown.Contains(t.FullName)).OrderBy(t => t.Name, StringComparer.Ordinal)];
        }
    }

    public string? UndoName => _undoStack.Count == 0 ? null : _undoStack[^1].Name;

    public string? RedoName => _redoStack.Count == 0 ? null : _redoStack[^1].Name;

    /// <summary>Classes or records the type may derive from: same kind, not sealed or static, not itself.</summary>
    public IReadOnlyList<DiagramType> BaseCandidates(DiagramType type) =>
        [.. Model.Types.Where(t => t.Kind == type.Kind && t.FullName != type.FullName && CanBeBase(t))];

    private static bool CanBeBase(DiagramType type) =>
        !type.Modifiers.HasFlag(TypeModifiers.Sealed) && !type.Modifiers.HasFlag(TypeModifiers.Static);

    /// <summary>Interfaces the type may implement (or, for an interface, extend).</summary>
    public IReadOnlyList<DiagramType> InterfaceCandidates(DiagramType type) =>
        [.. Model.Types.Where(t => t.Kind == DiagramTypeKind.Interface && t.FullName != type.FullName)];

    partial void OnShowsExternalChanged(bool value) => UpdateGeometry();

    private void UpdateGeometry()
    {
        var all = DiagramGeometry.Boxes(Model, Layout);
        Boxes = ShowsExternal ? all : [.. all.Where(b => !b.Type.IsExternal)];
        Edges = DiagramGeometry.Edges(Model, Boxes);
    }

    // Creating and refreshing
    /// <summary>Creates a diagram showing all source types below a folder (and the external types they derive from).</summary>
    public static async Task Create(string path, string typesBelow, Workspace workspace)
    {
        if (workspace.Engine is not { } engine || !workspace.IsEngineReady)
        {
            throw EngineException.NotRunning();
        }

        var model = await engine.DiagramModel();
        var prefix = RealPath.Resolve(typesBelow) + "/";
        var names = model.Types.Where(t => !t.IsExternal && t.FilePaths.Any(p => p.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(t => t.FullName).ToHashSet();
        foreach (var relation in model.Relations)
        {
            if (names.Contains(relation.From) && model.Find(relation.To)?.IsExternal == true)
            {
                names.Add(relation.To);
            }
        }

        var document = new DiagramDocument([.. names.Order(StringComparer.Ordinal).Select(n => new TypePlacement(n))]);
        AutoLayout.Arrange(document, model, all: true).Save(path, ClassDiagramTypes(model, path));
    }

    /// <summary>Element names and source files for types written into a <c>.cd</c> file for the first time.</summary>
    private static Dictionary<string, ClassDiagramFile.TypeInfo> ClassDiagramTypes(DiagramModel model, string path)
    {
        var result = new Dictionary<string, ClassDiagramFile.TypeInfo>();
        if (!ClassDiagramFile.IsClassDiagram(path))
        {
            return result;
        }

        var folder = System.IO.Path.GetDirectoryName(path) ?? "/";
        foreach (var type in model.Types.Where(t => !t.IsExternal))
        {
            result.TryAdd(type.FullName, ClassDiagramFile.TypeInfo.From(type, folder));
        }

        return result;
    }

    public void ScheduleRefresh(bool immediately = false)
    {
        _refreshDelay?.Cancel();
        var source = new CancellationTokenSource();
        _refreshDelay = source;
        _ = RefreshAfterDelay(immediately, source.Token);
    }

    private async Task RefreshAfterDelay(bool immediately, CancellationToken token)
    {
        try
        {
            if (!immediately)
            {
                await Task.Delay(350, token);
            }
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await Refresh();
        }
    }

    public async Task Refresh()
    {
        if (_workspace.Engine is not { } engine || !_workspace.IsEngineReady)
        {
            return;
        }

        IsLoading = Model.Types.Count == 0;
        try
        {
            foreach (var document in _workspace.OpenDocuments.Where(d => d.IsCSharp))
            {
                _workspace.SyncNow(document);
            }

            DiagramModel fresh;
            try
            {
                fresh = await engine.DiagramModel();
            }
            catch (EngineException)
            {
                return;
            }

            Model = fresh;
            var arranged = AutoLayout.Arrange(Layout, fresh, all: false);
            if (!arranged.Equals(Layout))
            {
                SaveLayout(arranged);
            }

            if (Selection is { } selection && fresh.Find(selection) is null)
            {
                Selection = null;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>The active tool of the diagram tool bar (Swift: <c>DiagramEditorModel.Tool</c>).</summary>
public sealed record DiagramTool
{
    private DiagramTool(string name, DiagramTypeKind? kind = null)
    {
        ToolName = name;
        Kind = kind;
    }

    public static DiagramTool Select { get; } = new("select");

    public static DiagramTool Inherit { get; } = new("inherit");

    public string ToolName { get; }

    /// <summary>Gets the kind of type the create tool places (null for select and inherit).</summary>
    public DiagramTypeKind? Kind { get; }

    public bool IsCreate => Kind is not null;

    public static DiagramTool Create(DiagramTypeKind kind) => new("create", kind);
}
