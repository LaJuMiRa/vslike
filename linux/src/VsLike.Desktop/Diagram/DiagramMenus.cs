using Avalonia.Controls;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>Titles, placeholders and examples per member kind (MemberKindText in DiagramEditorView.swift).</summary>
public static class MemberKindText
{
    public static string Title(MemberKind kind) => kind switch
    {
        MemberKind.Field => L.T("Field"),
        MemberKind.Property => L.T("Property"),
        MemberKind.Method => L.T("Method"),
        MemberKind.Constructor => L.T("Constructor"),
        MemberKind.Event => L.T("Event"),
        _ => L.T("Enum Value"),
    };

    public static string Placeholder(MemberKind kind) => kind switch
    {
        MemberKind.Field => "- _items: List<Item>",
        MemberKind.Property => "+ Name: string",
        MemberKind.Method => "+ Total(withTax: bool): decimal",
        MemberKind.Constructor => "+ (name: string)",
        MemberKind.Event => "+ Changed: EventHandler",
        _ => "Active = 1",
    };

    public static string Example(MemberKind kind) => kind is MemberKind.Constructor or MemberKind.EnumValue
        ? L.T("UML notation: + public, - private, # protected, ~ internal. Example: %@", Placeholder(kind))
        : L.T("Only the name is required: without a type it becomes int (methods: void, events: EventHandler), without a symbol public. Optional UML notation: + public, - private, # protected, ~ internal. Example: %@", Placeholder(kind));

    /// <summary>Member kinds a type can get: only enum values for enums.</summary>
    public static IReadOnlyList<MemberKind> KindsFor(DiagramType type) => type.Kind == DiagramTypeKind.Enum
        ? [MemberKind.EnumValue]
        : [MemberKind.Property, MemberKind.Method, MemberKind.Field, MemberKind.Constructor, MemberKind.Event];
}

/// <summary>The context menus of the diagram (TypeContextMenu and CanvasContextMenu in DiagramEditorView.swift).</summary>
internal static class DiagramMenus
{
    /// <summary>Right-click on a type box.</summary>
    public static List<Control> TypeMenu(DiagramEditorModel model, DiagramType type, bool collapsed)
    {
        var items = new List<Control>();
        if (!type.IsExternal)
        {
            items.Add(Item(L.T("Open in Editor"), "doc.text", () => _ = model.OpenInEditor(type)));
            items.Add(Submenu(L.T("Add Member"), "plus", [.. MemberKindText.KindsFor(type).Select(kind => Item(MemberKindText.Title(kind), null, () => PromptMember(model, type, kind)))]));
            if (type.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record)
            {
                items.Add(Submenu(L.T("Base Class"), "arrow.up.square", BaseClassItems(model, type)));
            }

            if (type.Kind != DiagramTypeKind.Enum)
            {
                var title = type.Kind == DiagramTypeKind.Interface ? L.T("Extends Interfaces") : L.T("Implements Interfaces");
                items.Add(Submenu(title, "i.square", InterfaceItems(model, type)));
            }

            items.Add(Item(L.T("Rename…"), "pencil", () => PromptRename(model, type)));
            items.Add(Item(collapsed ? L.T("Expand") : L.T("Collapse"), "rectangle.compress.vertical", () => model.ToggleCollapsed(type.FullName)));
            items.Add(new Separator());
        }

        items.Add(Item(L.T("Remove from Diagram"), "eye.slash", () => model.RemoveFromDiagram(type.FullName)));
        if (!type.IsExternal)
        {
            items.Add(Item(L.T("Delete from Code…"), "trash", () => _ = model.DeleteType(type)));
        }

        return items;
    }

    /// <summary>Right-click on the empty canvas: create types at the click position, add existing types, arrange.</summary>
    public static List<Control> CanvasMenu(DiagramEditorModel model, Action<DiagramTypeKind> newType, Action arrange, Action fit) =>
    [
        Item(L.T("New Class…"), "c.square", () => newType(DiagramTypeKind.Class)),
        Item(L.T("New Interface…"), "i.square", () => newType(DiagramTypeKind.Interface)),
        Item(L.T("New Record…"), "r.square", () => newType(DiagramTypeKind.Record)),
        Item(L.T("New Struct…"), "s.square", () => newType(DiagramTypeKind.Struct)),
        Item(L.T("New Enum…"), "e.square", () => newType(DiagramTypeKind.Enum)),
        new Separator(),
        Submenu(L.T("Add Existing Type"), "plus.rectangle.on.rectangle", AddExistingItems(model)),
        Item(L.T("Arrange automatically"), "wand.and.rays", arrange),
        Item(L.T("Fit to window"), "arrow.up.left.and.down.right.magnifyingglass", fit),
    ];

    /// <summary>"Add Existing Type": all missing source types, or a note that every type is shown.</summary>
    public static List<Control> AddExistingItems(DiagramEditorModel model)
    {
        var missing = model.MissingTypes;
        if (missing.Count == 0)
        {
            return [Note(L.T("All types are on the diagram"))];
        }

        var items = new List<Control>
        {
            Item(L.T("Add All"), null, () => model.AddExisting(missing.Select(t => t.FullName))),
            new Separator(),
        };
        items.AddRange(missing.Select(type => Item(type.DisplayName, null, () => model.AddExisting([type.FullName]))));
        return items;
    }

    public static void PromptNewType(Workspace workspace, DiagramEditorModel model, DiagramTypeKind kind, CGPoint point) =>
        workspace.Prompt = new PromptRequest(
            L.T("New %@", kind.Keyword()),
            L.T("Creates a file in %@ with the namespace of the folder.", Path.GetFileName(model.NewTypeFolder)),
            kind == DiagramTypeKind.Interface ? "IShape" : "Shape",
            L.T("Create"),
            name => model.CreateType(kind, name, point));

    public static MenuItem Item(string title, string? symbol, Action action)
    {
        var item = new MenuItem { Header = title, Icon = symbol is null ? null : new SymbolIcon(symbol, 14) };
        item.Click += (_, _) => action();
        return item;
    }

    private static List<Control> BaseClassItems(DiagramEditorModel model, DiagramType type)
    {
        var candidates = model.BaseCandidates(type);
        var current = type.BaseType ?? string.Empty;
        var names = candidates.Select(c => c.DisplayName).ToList();
        if (type.BaseType is { } baseType && !names.Contains(baseType))
        {
            names.Add(baseType);
        }

        var items = new List<Control> { Choice(L.T("None"), current.Length == 0, () => SetBase(model, type, null)) };
        items.AddRange(names.Select(name => Choice(name, name == current, () => SetBase(model, type, name))));
        return items;
    }

    private static void SetBase(DiagramEditorModel model, DiagramType type, string? baseType) =>
        _ = model.SetBaseList(type, baseType, type.Interfaces);

    private static List<Control> InterfaceItems(DiagramEditorModel model, DiagramType type)
    {
        var candidates = model.InterfaceCandidates(type);
        if (candidates.Count == 0)
        {
            return [Note(L.T("The project has no interfaces"))];
        }

        return [.. candidates.Select(candidate => InterfaceToggle(model, type, candidate.DisplayName))];
    }

    private static Control InterfaceToggle(DiagramEditorModel model, DiagramType type, string name)
    {
        var isOn = type.Interfaces.Contains(name);
        var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = isOn };
        item.Click += (_, _) =>
        {
            IReadOnlyList<string> list = isOn ? [.. type.Interfaces.Where(i => i != name)] : [.. type.Interfaces, name];
            _ = model.SetBaseList(type, type.BaseType, list);
        };
        return item;
    }

    private static MenuItem Choice(string title, bool isSelected, Action action)
    {
        var item = new MenuItem { Header = title, ToggleType = MenuItemToggleType.Radio, IsChecked = isSelected };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Submenu(string title, string? symbol, List<Control> items)
    {
        var menu = new MenuItem { Header = title, Icon = symbol is null ? null : new SymbolIcon(symbol, 14) };
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private static MenuItem Note(string text) => new() { Header = text, IsEnabled = false };

    private static void PromptMember(DiagramEditorModel model, DiagramType type, MemberKind kind) =>
        model.Workspace.Prompt = new PromptRequest(
            L.T("New %@", MemberKindText.Title(kind)),
            MemberKindText.Example(kind),
            MemberKindText.Placeholder(kind),
            L.T("Add"),
            text => model.AddMember(type, kind, text));

    private static void PromptRename(DiagramEditorModel model, DiagramType type) =>
        model.Workspace.Prompt = new PromptRequest(
            L.T("Rename %@", type.Name),
            L.T("All references are updated."),
            type.Name,
            L.T("Rename"),
            name => model.RenameType(type, name),
            InitialValue: type.Name);
}
