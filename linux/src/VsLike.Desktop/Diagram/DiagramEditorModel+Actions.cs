using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

// Layout actions, code actions and undo of the class diagram (second half of DiagramEditorModel.swift).
public sealed partial class DiagramEditorModel
{
    // Layout actions
    public void Move(string fullName, CGPoint point)
    {
        if (Layout.Find(fullName) is not { } placement)
        {
            return;
        }

        var x = Layout.SnapToGrid ? BoxMetrics.Snap(point.X) : point.X;
        var y = Layout.SnapToGrid ? BoxMetrics.Snap(point.Y) : point.Y;
        if (x == placement.X && y == placement.Y)
        {
            return;
        }

        Commit(L.T("Move"), [], Layout.With(placement.Moved(x, y)));
    }

    public void ToggleCollapsed(string fullName)
    {
        if (Layout.Find(fullName) is { } placement)
        {
            Commit(L.T("Collapse"), [], Layout.With(placement.Toggled()));
        }
    }

    public void ArrangeAll() => Commit(L.T("Arrange"), [], AutoLayout.Arrange(Layout, Model, all: true));

    public void AddExisting(IEnumerable<string> names)
    {
        var all = names.ToHashSet();
        foreach (var relation in Model.Relations)
        {
            if (all.Contains(relation.From) && relation.Kind is RelationKind.Inheritance or RelationKind.Implementation
                && Model.Find(relation.To)?.IsExternal == true)
            {
                all.Add(relation.To);
            }
        }

        var next = Layout;
        foreach (var name in all.Order(StringComparer.Ordinal))
        {
            if (next.Find(name) is null)
            {
                next = next.With(new TypePlacement(name));
            }
        }

        Commit(L.T("Add Types"), [], AutoLayout.Arrange(next, Model, all: false));
    }

    public void RemoveFromDiagram(string fullName)
    {
        Commit(L.T("Remove from Diagram"), [], Layout.Without(fullName));
        if (Selection == fullName)
        {
            Selection = null;
        }
    }

    /// <summary>
    /// Remembers zoom and scroll position. Written half a second after the last change, so that zooming and panning
    /// do not rewrite the <c>.cd</c> file (and redraw every box) on each wheel or touchpad event.
    /// </summary>
    public void SetView(double zoom, CGPoint offset)
    {
        _viewSaveDelay?.Cancel();
        var source = new CancellationTokenSource();
        _viewSaveDelay = source;
        _ = SaveViewAfterDelay(zoom, offset, source.Token);
    }

    private async Task SaveViewAfterDelay(double zoom, CGPoint offset, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var next = Layout with { Zoom = zoom, OffsetX = offset.X, OffsetY = offset.Y };
        if (!token.IsCancellationRequested && !next.Equals(Layout))
        {
            SaveLayout(next);
        }
    }

    public void SetGrid(bool show, bool snap) => SaveLayout(Layout with { ShowGrid = show, SnapToGrid = snap });

    // Code actions
    /// <summary>Creates <c>&lt;Name&gt;.cs</c> in <see cref="NewTypeFolder"/> with the namespace from RootNamespace plus the folder.</summary>
    public async Task CreateType(DiagramTypeKind kind, string name, CGPoint point)
    {
        if (_workspace.Engine is not { } engine)
        {
            return;
        }

        try
        {
            var created = await engine.NewType(kind, name, NewTypeFolder);
            if (File.Exists(created.Path))
            {
                _workspace.Alert = new AlertMessage(L.T("Class Diagram"), L.T("%@ already exists.", System.IO.Path.GetFileName(created.Path)));
                return;
            }

            var placement = new TypePlacement(created.FullName, BoxMetrics.Snap(point.X), BoxMetrics.Snap(point.Y));
            await Change(L.T("Create %@", name), new Dictionary<string, string?> { [created.Path] = created.Text }, Layout.With(placement));
            Selection = created.FullName;
        }
        catch (EngineException ex)
        {
            _workspace.Alert = new AlertMessage(L.T("Class Diagram"), ex.Message);
        }
    }

    public async Task AddMember(DiagramType type, MemberKind kind, string text)
    {
        if (_workspace.Engine is not { } engine)
        {
            return;
        }

        if (await TryParse(engine, kind, text, type.Name) is not { } member)
        {
            _workspace.Alert = new AlertMessage(L.T("Class Diagram"), L.T("“%@” is not a valid member. Example: + Total(withTax: bool): decimal", text));
            return;
        }

        await EditTypeFile(type, L.T("Add %@", member.Name), null, false, source => engine.AddMember(source, type.FullName, member));
    }

    public async Task UpdateMember(DiagramMember old, DiagramType type, string text)
    {
        if (_workspace.Engine is not { } engine)
        {
            return;
        }

        if (await TryParse(engine, old.Kind, text, type.Name) is not { } parsed)
        {
            _workspace.Alert = new AlertMessage(L.T("Class Diagram"), L.T("“%@” is not a valid member.", text));
            return;
        }

        var member = new DiagramMember(parsed.Kind, parsed.Name, parsed.Type, parsed.Access, old.Modifiers, old.Accessors, parsed.Parameters, parsed.Value);
        await EditTypeFile(type, L.T("Change %@", member.Name), old.Signature, false, source => engine.UpdateMember(source, type.FullName, old.Signature, member));
    }

    public async Task RemoveMember(DiagramMember member, DiagramType type)
    {
        if (_workspace.Engine is { } engine)
        {
            await EditTypeFile(type, L.T("Delete %@", member.Name), member.Signature, false, source => engine.RemoveMember(source, type.FullName, member.Signature));
        }
    }

    public async Task SetModifiers(DiagramType type, Access access, TypeModifiers modifiers)
    {
        if (_workspace.Engine is { } engine)
        {
            await EditTypeFile(type, L.T("Change Modifiers"), null, false, source => engine.SetModifiers(source, type.FullName, access, modifiers));
        }
    }

    public async Task SetBaseList(DiagramType type, string? baseType, IReadOnlyList<string> interfaces)
    {
        if (_workspace.Engine is { } engine)
        {
            await EditTypeFile(type, L.T("Change Base Types"), null, true, source => engine.SetBaseList(source, type.FullName, baseType, interfaces));
        }
    }

    /// <summary>A drawn connection: base class (same kind, not sealed or static) or interface implementation.</summary>
    public async Task Connect(string from, string to)
    {
        if (Model.Find(from) is not { IsExternal: false } source || Model.Find(to) is not { } target || from == to)
        {
            return;
        }

        var targetName = target.DisplayName;
        if (target.Kind == DiagramTypeKind.Interface)
        {
            if (!source.Interfaces.Contains(targetName))
            {
                await SetBaseList(source, source.BaseType, [.. source.Interfaces, targetName]);
            }
        }
        else if (source.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record && target.Kind == source.Kind
            && !target.Modifiers.HasFlag(TypeModifiers.Sealed) && !target.Modifiers.HasFlag(TypeModifiers.Static))
        {
            await SetBaseList(source, targetName, source.Interfaces);
        }
        else
        {
            _workspace.Alert = new AlertMessage(L.T("Class Diagram"), L.T("%@ cannot derive from %@.", source.Name, target.Name));
        }
    }

    /// <summary>Deletes a type from the code after confirmation (the whole file when it held only this type).</summary>
    public async Task DeleteType(DiagramType type)
    {
        if (_workspace.Engine is not { } engine || type.IsExternal || type.FilePaths.Count == 0)
        {
            RemoveFromDiagram(type.FullName);
            return;
        }

        var references = await CountReferences(engine, type);
        var question = L.T("Delete %@?", type.Name) + "\n\n" + (references == 0
            ? L.T("The type is removed from the code.")
            : L.T("The type is removed from the code. %lld references to it will no longer compile.", references));
        if (_workspace.Dialogs is not { } dialogs || !await dialogs.Confirm(question, L.T("Delete")))
        {
            return;
        }

        var changes = new Dictionary<string, string?>();
        foreach (var path in type.FilePaths)
        {
            changes[path] = await Try(() => engine.RemoveType(Read(path), type.FullName));
        }

        await Change(L.T("Delete %@", type.Name), changes, Layout.Without(type.FullName));
        Selection = null;
    }

    /// <summary>Renames a type with Roslyn (all references) and its file when the file is named after the type.</summary>
    public async Task RenameType(DiagramType type, string newName)
    {
        if (_workspace.Engine is not { } engine || type.IsExternal || type.FilePaths.Count == 0 || newName == type.Name || newName.Length == 0)
        {
            return;
        }

        var file = type.FilePaths[0];
        try
        {
            var located = await engine.Locate(Read(file), type.FullName, null);
            var edit = await engine.Rename(file, located.IdentifierOffset, newName);
            var changes = new Dictionary<string, string?>();
            foreach (var fileEdit in edit.Files)
            {
                changes[fileEdit.FilePath] = TextEditing.Apply(fileEdit.Edits, Read(fileEdit.FilePath));
            }

            if (System.IO.Path.GetFileNameWithoutExtension(file) == type.Name)
            {
                var renamed = changes.TryGetValue(file, out var changed) ? changed : Read(file);
                var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file) ?? "/", newName + ".cs");
                if (renamed is not null && !File.Exists(target))
                {
                    changes[file] = null;
                    changes[target] = renamed;
                }
            }

            var arity = type.TypeParameters.Count == 0 ? string.Empty : $"`{type.TypeParameters.Count}";
            var newFullName = type.FullName[..^(type.Name.Length + arity.Length)] + newName + arity;
            await Change(L.T("Rename %@", type.Name), changes, Layout.Renamed(type.FullName, newFullName));
            Selection = newFullName;
        }
        catch (EngineException ex)
        {
            _workspace.Alert = new AlertMessage(L.T("Rename"), ex.Message);
        }
    }

    public async Task OpenInEditor(DiagramType type, DiagramMember? member = null)
    {
        if (_workspace.Engine is not { } engine)
        {
            return;
        }

        foreach (var file in type.FilePaths)
        {
            var located = await Try(() => engine.Locate(Read(file), type.FullName, member?.Signature));
            if (located is { Line: > 0 })
            {
                _workspace.OpenFile(file, located.Line);
                return;
            }
        }
    }

    public string ExportSVG() => SVGExporter.Export(Model, Layout);

    // Undo
    public async Task Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var entry = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        await Write([.. entry.Files.Select(f => (f.Path, f.Before))]);
        SaveLayout(entry.LayoutBefore);
        _redoStack.Add(entry);
        UpdateUndoState();
        await Refresh();
    }

    public async Task Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var entry = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        await Write([.. entry.Files.Select(f => (f.Path, f.After))]);
        SaveLayout(entry.LayoutAfter);
        _undoStack.Add(entry);
        UpdateUndoState();
        await Refresh();
    }
}
