using VsLike.Kit;

namespace VsLike.Desktop;

// Types that Workspace.swift declares next to the Workspace class, plus the interfaces through which the workspace
// reaches the views (the Swift original holds the AppKit views directly).

/// <summary>A tab in the editor area: a code document or a class diagram.</summary>
public abstract record EditorTab
{
    public abstract Guid Id { get; }

    public abstract string Title { get; }

    /// <summary>Gets the SF Symbols name used on macOS; the views map it to an icon.</summary>
    public abstract string Symbol { get; }

    public virtual bool IsDirty => false;

    public virtual bool Equals(EditorTab? other) => other is not null && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();

    public sealed record Code(CodeDocument Document) : EditorTab
    {
        public override Guid Id => Document.Id;

        public override string Title => Document.Name;

        public override string Symbol => FileNode.SymbolForFile(Document.Name);

        public override bool IsDirty => Document.IsDirty;

        public bool Equals(Code? other) => base.Equals(other);

        public override int GetHashCode() => base.GetHashCode();
    }

    public sealed record Diagram(IDiagramEditor Editor) : EditorTab
    {
        public override Guid Id => Editor.Id;

        public override string Title => Editor.Name;

        public override string Symbol => "rectangle.3.group";

        public bool Equals(Diagram? other) => base.Equals(other);

        public override int GetHashCode() => base.GetHashCode();
    }
}

public enum SidebarTab
{
    Solution,
    Files,
    Search,
    Tests,
}

public enum BottomTab
{
    Problems,
    Output,
    Terminal,
    Debug,
}

public enum EngineState
{
    Idle,
    Starting,
    Loading,
    Ready,
    Failed,
}

/// <summary>State of the analysis engine; <see cref="Message"/> is set for <see cref="EngineState.Failed"/>.</summary>
public sealed record EngineStatus(EngineState State, string? Message = null)
{
    public static readonly EngineStatus Idle = new(EngineState.Idle);
    public static readonly EngineStatus Starting = new(EngineState.Starting);
    public static readonly EngineStatus Loading = new(EngineState.Loading);
    public static readonly EngineStatus Ready = new(EngineState.Ready);

    public static EngineStatus Failed(string message) => new(EngineState.Failed, message);
}

public enum StatusStyle
{
    Neutral,
    Progress,
    Success,
    Warning,
    Error,
}

/// <summary>A transient message for the status pill in the toolbar.</summary>
public sealed record StatusMessage(string Text, StatusStyle Style = StatusStyle.Neutral);

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

public sealed record AlertMessage(string Title, string Message)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>A text prompt (new file name, rename …) shown as a sheet.</summary>
public sealed record PromptRequest(string Title, string Message, string Placeholder, string ConfirmTitle, Func<string, Task> Action, string InitialValue = "")
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>A list of references or definitions shown in a popover-like sheet.</summary>
public sealed record ReferenceList(string Title, IReadOnlyList<SourceLocation> Locations)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// The code editor commands that menus, palette and toolbar send to the focused editor (<c>CodeTextView</c> on macOS).
/// Implemented by the editor view; <see cref="Workspace.ActiveEditor"/> holds the one with keyboard focus.
/// </summary>
public interface ICodeEditor
{
    void ShowQuickActions();

    void TriggerCompletion();

    void ShowSignatureHelp();

    void RenameSymbol();

    void ToggleComment();

    void GoToDefinition();

    void FindReferences();

    void PromptGoToLine();

    void ToggleBreakpointAtCaret();
}

/// <summary>
/// What the workspace needs from an open class diagram (<c>DiagramEditorModel</c> on macOS, ported by the diagram part).
/// </summary>
public interface IDiagramEditor
{
    Guid Id { get; }

    string Name { get; }

    /// <summary>Gets the diagram file (<c>.cd</c> or <c>.cdiagram.json</c>).</summary>
    string Path { get; }

    /// <summary>Re-reads the model from the engine after code changes (debounced unless <paramref name="immediately"/>).</summary>
    void ScheduleRefresh(bool immediately = false);

    Task Undo();

    Task Redo();
}

/// <summary>Creates diagram models; registered by the diagram part at startup (<see cref="Workspace.DiagramFactory"/>).</summary>
public interface IDiagramEditorFactory
{
    IDiagramEditor Open(string path, Workspace workspace);

    /// <summary>Creates a new diagram file showing all source types below <paramref name="typesBelow"/>.</summary>
    Task Create(string path, string typesBelow, Workspace workspace);
}

/// <summary>Modal questions the workspace asks (NSAlert on macOS); implemented by the main window.</summary>
public interface IWorkspaceDialogs
{
    Task<SaveChoice> ConfirmSave(IReadOnlyList<string> names);

    /// <summary>A yes/no question; returns true for the confirm button.</summary>
    Task<bool> Confirm(string message, string confirmTitle);
}
