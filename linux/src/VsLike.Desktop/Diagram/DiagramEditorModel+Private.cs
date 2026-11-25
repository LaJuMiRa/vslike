using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

// Writing files, undo bookkeeping and engine helpers of the class diagram (private part of DiagramEditorModel.swift).
public sealed partial class DiagramEditorModel
{
    /// <summary>Runs a writer on the file that declares the type (for partial types: the part with the member or base list).</summary>
    private async Task EditTypeFile(DiagramType type, string actionName, string? signature, bool baseList, Func<string, Task<string?>> edit)
    {
        if (_workspace.Engine is not { } engine || type.IsExternal)
        {
            return;
        }

        string? target = null;
        foreach (var file in type.FilePaths)
        {
            var located = await Try(() => engine.Locate(Read(file), type.FullName, signature));
            if (located is null || located.IdentifierOffset < 0 || (signature is not null && located.Line == 0))
            {
                continue;
            }

            if (baseList && type.FilePaths.Count > 1 && !Read(file).Contains(':', StringComparison.Ordinal) && file != type.FilePaths[^1])
            {
                continue;
            }

            target = file;
            break;
        }

        if (target is null)
        {
            return;
        }

        try
        {
            var updated = await edit(Read(target));
            await Change(actionName, new Dictionary<string, string?> { [target] = updated }, Layout);
        }
        catch (EngineException ex)
        {
            _workspace.Alert = new AlertMessage(L.T("Class Diagram"), ex.Message);
        }
    }

    private async Task Change(string name, IReadOnlyDictionary<string, string?> files, DiagramDocument next)
    {
        var changes = new List<FileChange>();
        foreach (var (path, after) in files)
        {
            var exists = File.Exists(path) || _workspace.OpenDocuments.Any(d => d.Path == path);
            var before = exists ? Read(path) : null;
            if (before != after)
            {
                changes.Add(new FileChange(path, before, after));
            }
        }

        await Write([.. changes.Select(c => (c.Path, c.After))]);
        Commit(name, changes, next);
        await Refresh();
    }

    private void Commit(string name, IReadOnlyList<FileChange> files, DiagramDocument next)
    {
        if (files.Count == 0 && next.Equals(Layout))
        {
            return;
        }

        _undoStack.Add(new UndoEntry(name, files, Layout, next));
        _redoStack.Clear();
        SaveLayout(next);
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        OnPropertyChanged(nameof(UndoName));
        OnPropertyChanged(nameof(RedoName));
    }

    private void SaveLayout(DiagramDocument next)
    {
        Layout = next;
        try
        {
            next.Save(Path, ClassDiagramTypes(Model, Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _workspace.Status = new StatusMessage(ex.Message, StatusStyle.Error);
        }
    }

    /// <summary>Writes file contents (null deletes). Open documents are edited in place so their tab and undo stay valid.</summary>
    private async Task Write(IReadOnlyList<(string Path, string? Text)> files)
    {
        foreach (var (path, text) in files)
        {
            var document = _workspace.OpenDocuments.FirstOrDefault(d => d.Path == path);
            if (text is not null)
            {
                await WriteText(path, text, document);
            }
            else
            {
                await DeleteFile(path, document);
            }
        }

        _workspace.RefreshTree();
    }

    private async Task WriteText(string path, string text, CodeDocument? document)
    {
        if (document is not null)
        {
            var edit = TextEditing.MinimalEdit(document.Text, text);
            document.Apply([edit], L.T("Class Diagram"));
            _workspace.Save(document);
            return;
        }

        var existed = File.Exists(path);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? "/");
            await File.WriteAllTextAsync(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _workspace.Status = new StatusMessage(ex.Message, StatusStyle.Error);
            return;
        }

        if (_workspace.Engine is { } engine)
        {
            if (existed)
            {
                engine.UpdateDocument(path, text);
            }
            else
            {
                await Try(() => engine.AddDocument(path, text));
            }
        }
    }

    private async Task DeleteFile(string path, CodeDocument? document)
    {
        if (document is not null && _workspace.Tabs.FirstOrDefault(t => t.Id == document.Id) is { } tab)
        {
            try
            {
                document.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file is deleted next anyway.
            }

            await _workspace.Close(tab);
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _workspace.Status = new StatusMessage(ex.Message, StatusStyle.Error);
        }

        if (_workspace.Engine is { } engine)
        {
            await Try(() => engine.RemoveDocument(path));
        }
    }

    private string Read(string path)
    {
        if (_workspace.OpenDocuments.FirstOrDefault(d => d.Path == path) is { } document)
        {
            return document.Text;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private async Task<int> CountReferences(EngineClient engine, DiagramType type)
    {
        var file = type.FilePaths[0];
        var offset = (await Try(() => engine.Locate(Read(file), type.FullName, null)))?.IdentifierOffset ?? -1;
        if (offset < 0)
        {
            return 0;
        }

        var references = await Try(() => engine.References(file, offset));
        return Math.Max(0, (references?.Count ?? 0) - type.FilePaths.Count);
    }

    private static async Task<DiagramMember?> TryParse(EngineClient engine, MemberKind kind, string text, string owner) =>
        await Try(() => engine.ParseMember(kind, text, owner));

    /// <summary>Swift's <c>try?</c> for engine requests: null when the engine reports an error.</summary>
    private static async Task<T?> Try<T>(Func<Task<T>> request)
        where T : class?
    {
        try
        {
            return await request();
        }
        catch (EngineException)
        {
            return null;
        }
    }

    private static async Task Try(Func<Task<bool>> request)
    {
        try
        {
            await request();
        }
        catch (EngineException)
        {
            // Swift: `_ = try? await …`.
        }
    }

    private sealed record FileChange(string Path, string? Before, string? After);

    private sealed record UndoEntry(string Name, IReadOnlyList<FileChange> Files, DiagramDocument LayoutBefore, DiagramDocument LayoutAfter);
}

/// <summary>Opens and creates diagram models for the workspace (<see cref="Workspace.DiagramFactory"/>).</summary>
public sealed class DiagramEditorFactory : IDiagramEditorFactory
{
    /// <summary>Registers the factory; called once at startup (also from tests).</summary>
    public static void Register() => Workspace.DiagramFactory ??= new DiagramEditorFactory();

    /// <summary>Registers the factory as soon as the app assembly loads, so the shell needs no extra startup call.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterOnLoad() => Register();

    public IDiagramEditor Open(string path, Workspace workspace) => new DiagramEditorModel(path, workspace);

    public Task Create(string path, string typesBelow, Workspace workspace) => DiagramEditorModel.Create(path, typesBelow, workspace);
}
