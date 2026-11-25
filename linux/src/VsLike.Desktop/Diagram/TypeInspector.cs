using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// The inspector of one selected type (TypeInspector and MemberRow in DiagramInspector.swift). Rebuilt when the type
/// changes in the code; the chosen member kind and the typed member text are kept.
/// </summary>
internal sealed class TypeInspector : StackPanel
{
    private readonly DiagramEditorModel _model;
    private DiagramType _type;
    private MemberKind _newMemberKind;
    private string _newMemberText = string.Empty;

    public TypeInspector(DiagramEditorModel model, DiagramType type)
    {
        _model = model;
        _type = type;
        _newMemberKind = type.Kind == DiagramTypeKind.Enum ? MemberKind.EnumValue : MemberKind.Property;
        Spacing = 18;
        Margin = new Thickness(12, 14);
        Build();
    }

    public string FullName => _type.FullName;

    public void Update(DiagramType type)
    {
        _type = type;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        Children.Add(InspectorRows.Section(null, IdentityRows()));
        if (!_type.IsExternal && _type.Kind != DiagramTypeKind.Enum)
        {
            Children.Add(InspectorRows.Section(L.T("Base Types"), BaseTypeRows()));
        }

        Children.Add(InspectorRows.Section(L.T("Members"), MemberRows()));
        if (!_type.IsExternal)
        {
            var actions = new List<Control>
            {
                InspectorRows.TextButton(L.T("Open in Editor"), "doc.text", () => _ = _model.OpenInEditor(_type)),
                InspectorRows.TextButton(L.T("Delete from Code…"), "trash", () => _ = _model.DeleteType(_type), destructive: true),
            };
            Children.Add(InspectorRows.Section(null, actions));
        }
    }

    private List<Control> IdentityRows()
    {
        var type = _type;
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new SymbolIcon(Icon(type.Kind), 22) { Foreground = DiagramColors.Brush(DiagramColors.ForKind(type.Kind)), VerticalAlignment = VerticalAlignment.Center });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = type.DisplayName, FontWeight = FontWeight.Bold });
        titles.Children.Add(new TextBlock
        {
            Text = type.Namespace.Length == 0 ? type.Kind.Keyword() : $"{type.Kind.Keyword()} · {type.Namespace}",
            FontSize = 10,
            Foreground = DiagramColors.Brush(DiagramColors.Secondary),
        });
        heading.Children.Add(titles);
        var rows = new List<Control> { heading };
        if (type.IsExternal)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            label.Children.Add(new SymbolIcon("lock", 13));
            label.Children.Add(new TextBlock { Text = L.T("External type (read-only)") });
            TextBlockForeground(label);
            rows.Add(label);
            return rows;
        }

        var name = new TextBox { Text = type.Name };
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = _model.RenameType(type, name.Text ?? string.Empty);
            }
        };
        rows.Add(InspectorRows.Labeled(L.T("Name"), name));
        IReadOnlyList<(Access, string)> accesses = [(Access.Public, Access.Public.Keyword()), (Access.Internal, Access.Internal.Keyword())];
        rows.Add(InspectorRows.Picker(L.T("Access"), accesses, type.Access, access => _ = _model.SetModifiers(type, access, type.Modifiers)));
        if (type.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record)
        {
            rows.Add(ModifierToggle("abstract", TypeModifiers.Abstract));
            rows.Add(ModifierToggle("sealed", TypeModifiers.Sealed));
            rows.Add(ModifierToggle("static", TypeModifiers.Static));
        }

        rows.Add(ModifierToggle("partial", TypeModifiers.Partial));
        return rows;
    }

    private List<Control> BaseTypeRows()
    {
        var type = _type;
        var rows = new List<Control>();
        var interfaces = _model.InterfaceCandidates(type);
        if (type.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record)
        {
            var options = new List<(string Value, string Title)> { (string.Empty, L.T("None")) };
            options.AddRange(_model.BaseCandidates(type).Select(t => (t.DisplayName, t.DisplayName)));
            if (type.BaseType is { } baseType && options.All(o => o.Value != baseType))
            {
                options.Add((baseType, baseType));
            }

            void SetBase(string value) => _ = _model.SetBaseList(type, value.Length == 0 ? null : value, type.Interfaces);
            rows.Add(InspectorRows.Picker(L.T("Base class"), options, type.BaseType ?? string.Empty, SetBase));
        }

        foreach (var candidate in interfaces)
        {
            var name = candidate.DisplayName;
            rows.Add(InspectorRows.Toggle(name, type.Interfaces.Contains(name), on =>
            {
                IReadOnlyList<string> list = on ? [.. type.Interfaces, name] : [.. type.Interfaces.Where(i => i != name)];
                _ = _model.SetBaseList(type, type.BaseType, list);
            }));
        }

        foreach (var external in type.Interfaces.Where(n => interfaces.All(c => c.DisplayName != n)))
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            label.Children.Add(new SymbolIcon("i.square", 14));
            label.Children.Add(new TextBlock { Text = external });
            TextBlockForeground(label);
            rows.Add(label);
        }

        return rows;
    }

    private List<Control> MemberRows()
    {
        var rows = new List<Control>(_type.Members.Select(MemberRow));
        if (_type.IsExternal)
        {
            return rows;
        }

        rows.Add(NewMemberRow());
        rows.Add(new TextBlock
        {
            Text = L.T("Only the name is required (default: public, int, methods void). Optional: + public  - private  # protected  ~ internal"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = DiagramColors.Brush(DiagramColors.Tertiary),
        });
        return rows;
    }

    private Control NewMemberRow()
    {
        var kinds = MemberKindText.KindsFor(_type);
        var kind = new ComboBox { ItemsSource = kinds.Select(MemberKindText.Title).ToList(), SelectedIndex = Math.Max(0, kinds.ToList().IndexOf(_newMemberKind)) };
        var text = new TextBox
        {
            Text = _newMemberText,
            PlaceholderText = MemberKindText.Placeholder(_newMemberKind),
            FontFamily = new FontFamily(Preferences.EditorFontFamily),
            FontSize = InspectorRows.MonoSize,
        };
        var add = InspectorRows.IconButton("plus.circle.fill", null, AddMember);
        add.IsEnabled = _newMemberText.Length > 0;
        add.Opacity = add.IsEnabled ? 1 : 0.4;
        kind.SelectionChanged += (_, _) =>
        {
            _newMemberKind = kinds[Math.Max(0, kind.SelectedIndex)];
            text.PlaceholderText = MemberKindText.Placeholder(_newMemberKind);
        };
        text.TextChanged += (_, _) =>
        {
            _newMemberText = text.Text ?? string.Empty;
            add.IsEnabled = _newMemberText.Length > 0;
            add.Opacity = add.IsEnabled ? 1 : 0.4;
        };
        text.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddMember();
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(kind);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(add, 2);
        grid.Children.Add(add);
        return grid;
    }

    private void AddMember()
    {
        var text = _newMemberText;
        if (text.Length == 0)
        {
            return;
        }

        _newMemberText = string.Empty;
        _ = _model.AddMember(_type, _newMemberKind, text);
    }

    /// <summary>One member: UML line (double click edits), "Show in code" and "Delete member".</summary>
    private Control MemberRow(DiagramMember member)
    {
        var type = _type;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        var line = new TextBlock
        {
            Text = member.UmlLine,
            FontFamily = new FontFamily(Preferences.EditorFontFamily),
            FontSize = InspectorRows.MonoSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        grid.Children.Add(line);
        if (!type.IsExternal)
        {
            line.DoubleTapped += (_, _) => EditMember(grid, line, member);
            var show = InspectorRows.IconButton("arrow.right.circle", L.T("Show in code"), () => _ = _model.OpenInEditor(type, member));
            var remove = InspectorRows.IconButton("minus.circle", L.T("Delete member"), () => _ = _model.RemoveMember(member, type));
            Grid.SetColumn(show, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(show);
            grid.Children.Add(remove);
        }

        return grid;
    }

    private void EditMember(Grid grid, TextBlock line, DiagramMember member)
    {
        var type = _type;
        var field = new TextBox { Text = member.EditText, FontFamily = line.FontFamily, FontSize = InspectorRows.MonoSize };
        void Finish(bool commit)
        {
            grid.Children.Remove(field);
            line.IsVisible = true;
            if (commit && field.Text is { } text && text != member.EditText)
            {
                _ = _model.UpdateMember(member, type, text);
            }
        }

        field.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        line.IsVisible = false;
        grid.Children.Add(field);
        field.Focus();
        field.SelectAll();
    }

    private Control ModifierToggle(string title, TypeModifiers flag) =>
        InspectorRows.Toggle(title, _type.Modifiers.HasFlag(flag), on => _ = _model.SetModifiers(_type, _type.Access, Toggled(_type.Modifiers, flag, on)));

    /// <summary>Applies one modifier switch and removes the modifiers that cannot be combined with it.</summary>
    internal static TypeModifiers Toggled(TypeModifiers modifiers, TypeModifiers flag, bool on)
    {
        var result = on ? modifiers | flag : modifiers & ~flag;
        if (on && flag == TypeModifiers.Static)
        {
            result &= ~(TypeModifiers.Abstract | TypeModifiers.Sealed);
        }

        if (on && flag is TypeModifiers.Abstract or TypeModifiers.Sealed)
        {
            result &= ~TypeModifiers.Static;
        }

        if (on && flag == TypeModifiers.Abstract)
        {
            result &= ~TypeModifiers.Sealed;
        }

        if (on && flag == TypeModifiers.Sealed)
        {
            result &= ~TypeModifiers.Abstract;
        }

        return result;
    }

    private static string Icon(DiagramTypeKind kind) => kind switch
    {
        DiagramTypeKind.Class => "c.square.fill",
        DiagramTypeKind.Interface => "i.square.fill",
        DiagramTypeKind.Struct or DiagramTypeKind.RecordStruct => "s.square.fill",
        DiagramTypeKind.Record => "r.square.fill",
        _ => "e.square.fill",
    };

    private static void TextBlockForeground(StackPanel label) =>
        Avalonia.Controls.Documents.TextElement.SetForeground(label, DiagramColors.Brush(DiagramColors.Secondary));
}
