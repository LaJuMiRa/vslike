using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using VsLike.App.Editor;
using VsLike.App.Intelligence;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Diagram.Code;
using VsLike.Diagram.Export;
using VsLike.Diagram.Layout;
using VsLike.Diagram.Model;
using VsLike.Projects;

namespace VsLike.App.Diagram;

/// <summary>
/// Ein Klassendiagramm-Tab. Typen und Mitglieder kommen immer aus dem Code (Roslyn-Reader); jede Diagrammaktion
/// schreibt per <see cref="DiagramWriter"/> in die betroffene Datei und wird als ein Schritt rückgängig machbar.
/// Layout und Ansicht liegen in der <c>.cdiagram.json</c>.
/// </summary>
public sealed partial class DiagramDocumentViewModel : Document
{
    private readonly DiagramServices _services;
    private readonly Stack<DiagramUndoEntry> _undo = new();
    private readonly Stack<DiagramUndoEntry> _redo = new();
    private CancellationTokenSource? _refresh;

    public DiagramDocumentViewModel(string path, DiagramServices services)
    {
        FilePath = Path.GetFullPath(path);
        _services = services;
        Id = "diagram:" + FilePath;
        Title = DiagramDocument.NameOf(FilePath);
        Layout = DiagramDocument.Load(FilePath);
    }

    /// <summary>Raised whenever model or layout changed (the view redraws).</summary>
    public event EventHandler? Changed;

    public string FilePath { get; }

    public DiagramModel Model { get; private set; } = DiagramModel.Empty;

    public DiagramDocument Layout { get; private set; }

    public string? Selected { get; private set; }

    public DiagramType? SelectedType => Selected is null ? null : Model.Find(Selected);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string Folder => Path.GetDirectoryName(FilePath)!;

    /// <summary>Re-reads the model from the current solution snapshot (keeps positions), placing new entries automatically.</summary>
    public async Task RefreshAsync()
    {
        if (_services.Solution() is not { } solution)
        {
            return;
        }

        Model = await Task.Run(() => DiagramReader.ReadAsync(solution, CancellationToken.None)).ConfigureAwait(true);
        var arranged = AutoLayout.Arrange(Layout, Model, all: false);
        if (!ReferenceEquals(arranged, Layout))
        {
            SaveLayout(arranged);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Debounced refresh after code changes (typing in an editor, external edits).</summary>
    public void ScheduleRefresh()
    {
        _refresh?.Cancel();
        var cancellation = _refresh = new CancellationTokenSource();
        _ = Task.Delay(300, cancellation.Token).ContinueWith(
            async _ => await RefreshAsync().ConfigureAwait(true),
            cancellation.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void Select(string? fullName)
    {
        Selected = fullName;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds existing types (and the external types they derive from) to the diagram.</summary>
    public void AddExisting(IEnumerable<string> fullNames)
    {
        var names = fullNames.ToHashSet(StringComparer.Ordinal);
        foreach (var relation in Model.Relations.Where(r => names.Contains(r.From) && r.Kind is RelationKind.Inheritance or RelationKind.Implementation && Model.Find(r.To)?.IsExternal == true).ToList())
        {
            names.Add(relation.To);
        }

        var layout = Layout;
        foreach (var name in names.Where(n => Layout.Find(n) is null && Model.Find(n) is not null))
        {
            layout = layout.WithPlacement(new TypePlacement(name, double.NaN, double.NaN));
        }

        Commit(Loc.Get("Diagram.Undo.AddExisting"), [], AutoLayout.Arrange(layout, Model, all: false));
    }

    public void Move(TypePlacement placement) => Commit(Loc.Get("Diagram.Undo.Move"), [], Layout.WithPlacement(placement));

    public void RemoveFromDiagram(string fullName) => Commit(Loc.Get("Diagram.Undo.RemoveFromDiagram"), [], Layout.Without(fullName));

    public void ToggleCollapsed(string fullName)
    {
        if (Layout.Find(fullName) is { } placement)
        {
            Commit(Loc.Get("Diagram.Undo.Move"), [], Layout.WithPlacement(placement with { Collapsed = !placement.Collapsed }));
        }
    }

    public void ArrangeAll() => Commit(Loc.Get("Diagram.Undo.Arrange"), [], AutoLayout.Arrange(Layout, Model, all: true));

    public void SetView(double zoom, double offsetX, double offsetY) => SaveLayout(Layout with { Zoom = zoom, OffsetX = offsetX, OffsetY = offsetY });

    public void SetGrid(bool showGrid, bool snap) => SaveLayout(Layout with { ShowGrid = showGrid, SnapToGrid = snap });

    /// <summary>Creates <c>&lt;Name&gt;.cs</c> in the diagram's folder with the namespace from RootNamespace + folder.</summary>
    public async Task<bool> CreateTypeAsync(DiagramTypeKind kind, string name, double x, double y)
    {
        var identifier = NamespaceResolver.ToIdentifier(name.Trim());
        var path = Path.Combine(Folder, identifier + ".cs");
        if (identifier.Length == 0 || File.Exists(path))
        {
            await _services.Dialogs.ShowMessageAsync(Loc.Get("Diagram.Title"), Loc.Format("Diagram.FileExists", Path.GetFileName(path))).ConfigureAwait(true);
            return false;
        }

        var project = _services.Projects().Where(p => Folder.StartsWith(Path.GetDirectoryName(p.FilePath)! + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Folder == Path.GetDirectoryName(p.FilePath)).MaxBy(p => p.FilePath.Length);
        var ns = project is null ? null : NamespaceResolver.Resolve(project.FilePath, project.RootNamespace, Folder);
        var type = new DiagramType(string.IsNullOrEmpty(ns) ? identifier : ns + "." + identifier, identifier, ns ?? string.Empty, kind);
        var text = CodeText.TypeFile(type, ns);
        var layout = Layout.WithPlacement(new TypePlacement(type.FullName, x, y));
        await ChangeAsync(Loc.Format("Diagram.Undo.CreateType", identifier), new Dictionary<string, string?> { [path] = text }, layout).ConfigureAwait(true);
        Select(type.FullName);
        return true;
    }

    public Task AddMemberAsync(string typeFullName, DiagramMember member)
        => EditTypeFileAsync(typeFullName, Loc.Format("Diagram.Undo.AddMember", member.Name), (source, _) => DiagramWriter.AddMember(source, typeFullName, member));

    public Task RemoveMemberAsync(string typeFullName, DiagramMember member)
        => EditTypeFileAsync(typeFullName, Loc.Format("Diagram.Undo.RemoveMember", member.Name), (source, _) => DiagramWriter.RemoveMember(source, typeFullName, member.Signature), member.Signature);

    public Task UpdateMemberAsync(string typeFullName, DiagramMember before, DiagramMember after)
        => EditTypeFileAsync(typeFullName, Loc.Format("Diagram.Undo.UpdateMember", after.Name), (source, _) => DiagramWriter.UpdateMember(source, typeFullName, before.Signature, after), before.Signature);

    public Task SetModifiersAsync(string typeFullName, Access access, TypeModifiers modifiers)
        => EditTypeFileAsync(typeFullName, Loc.Get("Diagram.Undo.Modifiers"), (source, _) => DiagramWriter.SetTypeModifiers(source, typeFullName, access, modifiers));

    public Task SetBaseListAsync(string typeFullName, string? baseType, IReadOnlyList<string> interfaces)
        => EditTypeFileAsync(typeFullName, Loc.Get("Diagram.Undo.BaseList"), (source, _) => DiagramWriter.SetBaseList(source, typeFullName, baseType, interfaces), baseList: true);

    /// <summary>Connection drawn from <paramref name="from"/> to <paramref name="to"/>: base class or interface (<c>: Basis, IFoo</c>).</summary>
    public async Task ConnectAsync(string from, string to)
    {
        if (Model.Find(from) is not { IsExternal: false } source || Model.Find(to) is not { } target)
        {
            return;
        }

        var targetName = target.DisplayName;
        if (target.Kind == DiagramTypeKind.Interface)
        {
            if (!source.InterfaceList.Contains(targetName))
            {
                await SetBaseListAsync(from, source.BaseType, [.. source.InterfaceList, targetName]).ConfigureAwait(true);
            }
        }
        else if (source.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record && target.Kind == source.Kind && !target.Modifiers.HasFlag(TypeModifiers.Sealed) && !target.Modifiers.HasFlag(TypeModifiers.Static))
        {
            await SetBaseListAsync(from, targetName, source.InterfaceList).ConfigureAwait(true);
        }
        else
        {
            await _services.Dialogs.ShowMessageAsync(Loc.Get("Diagram.Title"), Loc.Format("Diagram.CannotInherit", source.Name, target.Name)).ConfigureAwait(true);
        }
    }

    /// <summary>Deletes a type from the code (whole file when it is the only type) after confirmation.</summary>
    public async Task DeleteTypeAsync(string fullName)
    {
        if (Model.Find(fullName) is not { IsExternal: false } type || type.FileList.Count == 0)
        {
            RemoveFromDiagram(fullName);
            return;
        }

        var references = await CountReferencesAsync(type).ConfigureAwait(true);
        if (!await _services.Dialogs.ConfirmAsync(Loc.Get("Diagram.Title"), Loc.Format("Diagram.ConfirmDelete", type.Name, references)).ConfigureAwait(true))
        {
            return;
        }

        var changes = new Dictionary<string, string?>();
        foreach (var file in type.FileList)
        {
            changes[file] = DiagramWriter.RemoveType(await ReadAsync(file).ConfigureAwait(true), fullName);
        }

        await ChangeAsync(Loc.Format("Diagram.Undo.DeleteType", type.Name), changes, Layout.Without(fullName)).ConfigureAwait(true);
    }

    /// <summary>Renames a type with Roslyn (all references) and renames its file when it is named after the type.</summary>
    public async Task RenameTypeAsync(string fullName, string newName)
    {
        if (Model.Find(fullName) is not { IsExternal: false } type || type.FileList.Count == 0 || _services.Solution() is null)
        {
            return;
        }

        var file = type.FileList[0];
        var offset = IdentifierOffset(await ReadAsync(file).ConfigureAwait(true), type);
        var edit = await _services.Features.RenameAsync(file, offset, newName, CancellationToken.None).ConfigureAwait(true);
        var changes = new Dictionary<string, string?>();
        foreach (var fileEdit in edit.Files)
        {
            changes[fileEdit.FilePath] = WorkspaceEditApplier.ApplyToText(await ReadAsync(fileEdit.FilePath).ConfigureAwait(true), fileEdit.Edits);
        }

        if (Path.GetFileNameWithoutExtension(file) == type.Name && changes.TryGetValue(file, out var renamed))
        {
            var target = Path.Combine(Path.GetDirectoryName(file)!, newName + ".cs");
            if (!File.Exists(target))
            {
                changes[file] = null;
                changes[target] = renamed;
            }
        }

        var newFullName = fullName[..^type.Name.Length] + newName;
        if (type.TypeParameterList.Count > 0)
        {
            var arity = "`" + type.TypeParameterList.Count;
            newFullName = fullName[..^(type.Name.Length + arity.Length)] + newName + arity;
        }

        await ChangeAsync(Loc.Format("Diagram.Undo.Rename", type.Name, newName), changes, Layout.Renamed(fullName, newFullName)).ConfigureAwait(true);
        Select(newFullName);
    }

    public async Task OpenInEditorAsync(string fullName, int memberIndex)
    {
        if (Model.Find(fullName) is not { IsExternal: false } type || type.FileList.Count == 0)
        {
            return;
        }

        var member = memberIndex >= 0 && memberIndex < type.MemberList.Count ? type.MemberList[memberIndex] : null;
        foreach (var file in type.FileList)
        {
            var root = SyntaxLocator.Parse(await ReadAsync(file).ConfigureAwait(true));
            var declaration = SyntaxLocator.Types(root).FirstOrDefault(t => SyntaxLocator.FullNameOf(t) == fullName);
            if (declaration is null)
            {
                continue;
            }

            var node = member is null ? null : MemberNode(declaration, member.Signature);
            if (member is not null && node is null)
            {
                continue;
            }

            var line = (node ?? (Microsoft.CodeAnalysis.SyntaxNode)declaration).GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            await _services.Navigator.OpenAsync(file, line, 1).ConfigureAwait(true);
            return;
        }
    }

    public string ExportSvg() => SvgExporter.Export(Model, Layout);

    public async Task UndoAsync()
    {
        if (_undo.TryPop(out var entry))
        {
            await ApplyFilesAsync(entry.Files.Select(f => (f.Path, f.Before))).ConfigureAwait(true);
            SaveLayout(entry.LayoutBefore);
            _redo.Push(entry);
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    public async Task RedoAsync()
    {
        if (_redo.TryPop(out var entry))
        {
            await ApplyFilesAsync(entry.Files.Select(f => (f.Path, f.After))).ConfigureAwait(true);
            SaveLayout(entry.LayoutAfter);
            _undo.Push(entry);
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private static Microsoft.CodeAnalysis.SyntaxNode? MemberNode(Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax declaration, string signature)
    {
        try
        {
            return SyntaxLocator.FindMember(declaration, signature);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int IdentifierOffset(string source, DiagramType type)
    {
        var declaration = SyntaxLocator.FindType(SyntaxLocator.Parse(source), type.FullName);
        return declaration.Identifier.SpanStart;
    }

    private static TextEdit MinimalEdit(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        return new TextEdit(prefix, before.Length - prefix - suffix, after[prefix..^suffix]);
    }

    private async Task<int> CountReferencesAsync(DiagramType type)
    {
        var file = type.FileList[0];
        var offset = IdentifierOffset(await ReadAsync(file).ConfigureAwait(true), type);
        var references = await _services.Features.FindReferencesAsync(file, offset, CancellationToken.None).ConfigureAwait(true);
        return Math.Max(0, references.Count - type.FileList.Count);
    }

    /// <summary>Runs a writer on the file that declares the type (or the member / base list for partial types).</summary>
    private async Task EditTypeFileAsync(string fullName, string description, Func<string, string, string> edit, string? memberSignature = null, bool baseList = false)
    {
        if (Model.Find(fullName) is not { IsExternal: false } type || type.FileList.Count == 0)
        {
            return;
        }

        string? target = null;
        foreach (var file in type.FileList)
        {
            var root = SyntaxLocator.Parse(await ReadAsync(file).ConfigureAwait(true));
            var declaration = SyntaxLocator.Types(root).FirstOrDefault(t => SyntaxLocator.FullNameOf(t) == fullName);
            if (declaration is null)
            {
                continue;
            }

            if ((memberSignature is not null && MemberNode(declaration, memberSignature) is null) || (baseList && declaration.BaseList is null && type.FileList.Count > 1 && file != type.FileList[^1]))
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
            var source = await ReadAsync(target).ConfigureAwait(true);
            await ChangeAsync(description, new Dictionary<string, string?> { [target] = edit(source, target) }, Layout).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await _services.Dialogs.ShowMessageAsync(Loc.Get("Diagram.Title"), ex.Message).ConfigureAwait(true);
        }
    }

    private async Task ChangeAsync(string description, IReadOnlyDictionary<string, string?> changes, DiagramDocument layout)
    {
        var files = new List<FileChange>();
        foreach (var (path, after) in changes)
        {
            var before = File.Exists(path) || _services.OpenText(path) is not null ? await ReadAsync(path).ConfigureAwait(true) : null;
            if (before != after)
            {
                files.Add(new FileChange(path, before, after));
            }
        }

        await ApplyFilesAsync(files.Select(f => (f.Path, f.After))).ConfigureAwait(true);
        Commit(description, files, layout);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void Commit(string description, IReadOnlyList<FileChange> files, DiagramDocument layout)
    {
        if (files.Count == 0 && layout == Layout)
        {
            return;
        }

        _undo.Push(new DiagramUndoEntry(description, files, Layout, layout));
        _redo.Clear();
        SaveLayout(layout);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveLayout(DiagramDocument layout)
    {
        Layout = layout;
        layout.Save(FilePath);
    }

    private async Task ApplyFilesAsync(IEnumerable<(string Path, string? Text)> files)
    {
        foreach (var (path, text) in files)
        {
            var exists = File.Exists(path) || _services.OpenText(path) is not null;
            if (text is null)
            {
                await _services.DeleteFileAsync(path).ConfigureAwait(true);
            }
            else if (!exists)
            {
                await _services.Applier.ApplyAsync(new WorkspaceEdit([new FileEdit(path, [], text)])).ConfigureAwait(true);
            }
            else
            {
                var current = await ReadAsync(path).ConfigureAwait(true);
                await _services.Applier.ApplyAsync(new WorkspaceEdit([new FileEdit(path, [MinimalEdit(current, text)])])).ConfigureAwait(true);
            }
        }
    }

    private async Task<string> ReadAsync(string path)
        => _services.OpenText(path) ?? (await TextFileIO.ReadAsync(path).ConfigureAwait(true)).Text;
}
