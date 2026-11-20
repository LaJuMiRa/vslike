using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Diagram.Model;
using VsLike.Diagram.View;

namespace VsLike.App.Diagram;

/// <summary>Werkzeugleiste, Zeichenfläche und Eigenschaften eines Klassendiagramm-Tabs.</summary>
public sealed class DiagramView : UserControl, IEditTarget
{
    private readonly DiagramCanvas _canvas = new();
    private readonly StackPanel _properties = new() { Spacing = 8, Margin = new Thickness(12) };
    private readonly ToggleButton _selectTool;
    private readonly ToggleButton _connectTool;
    private DiagramTypeKind _placeKind;
    private DiagramDocumentViewModel? _viewModel;

    public DiagramView()
    {
        _selectTool = Toggle("Diagram.Tool.Select", () => SetTool(DiagramTool.Select));
        _connectTool = Toggle("Diagram.Tool.Connect", () => SetTool(DiagramTool.Connect));
        _selectTool.IsChecked = true;
        var toolbar = new WrapPanel { Margin = new Thickness(8, 6) };
        toolbar.Children.Add(_selectTool);
        toolbar.Children.Add(_connectTool);
        toolbar.Children.Add(new Separator { Width = 1, Height = 20, Margin = new Thickness(6, 0) });
        foreach (var kind in new[] { DiagramTypeKind.Class, DiagramTypeKind.Interface, DiagramTypeKind.Struct, DiagramTypeKind.Enum, DiagramTypeKind.Record, DiagramTypeKind.RecordStruct })
        {
            toolbar.Children.Add(Button("Diagram.New." + kind, () =>
            {
                _placeKind = kind;
                SetTool(DiagramTool.Place);
                return Task.CompletedTask;
            }));
        }

        toolbar.Children.Add(new Separator { Width = 1, Height = 20, Margin = new Thickness(6, 0) });
        toolbar.Children.Add(Button("Diagram.AddExisting", AddExistingAsync));
        toolbar.Children.Add(Button("Diagram.Arrange", () => Run(vm => vm.ArrangeAll())));
        toolbar.Children.Add(Button("Diagram.Fit", () =>
        {
            _canvas.FitToView();
            return Task.CompletedTask;
        }));
        toolbar.Children.Add(Button("Diagram.Grid", () => Run(vm => vm.SetGrid(!vm.Layout.ShowGrid, !vm.Layout.ShowGrid))));
        toolbar.Children.Add(Button("Diagram.ExportSvg", ExportSvgAsync));
        toolbar.Children.Add(Button("Diagram.ExportPng", ExportPngAsync));
        toolbar.Children.Add(Button("Command.Edit.Undo", () => _viewModel?.UndoAsync() ?? Task.CompletedTask));
        toolbar.Children.Add(Button("Command.Edit.Redo", () => _viewModel?.RedoAsync() ?? Task.CompletedTask));

        _canvas.SelectionChanged += (_, name) => _viewModel?.Select(name);
        _canvas.TypeMoved += (_, placement) => _viewModel?.Move(placement);
        _canvas.ViewChanged += (_, view) => _viewModel?.SetView(view.Zoom, view.OffsetX, view.OffsetY);
        _canvas.ConnectRequested += async (_, link) => await (_viewModel?.ConnectAsync(link.From, link.To) ?? Task.CompletedTask).ConfigureAwait(true);
        _canvas.OpenRequested += async (_, target) => await (_viewModel?.OpenInEditorAsync(target.FullName, target.MemberIndex) ?? Task.CompletedTask).ConfigureAwait(true);
        _canvas.DeleteRequested += (_, name) => _viewModel?.RemoveFromDiagram(name);
        _canvas.PlaceRequested += async (_, point) => await PlaceAsync(point.X, point.Y).ConfigureAwait(true);

        var side = new Border
        {
            Width = 300,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = _properties },
        };
        side.Bind(Border.BorderBrushProperty, side.GetResourceObservable("VsBorder"));
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(side, Avalonia.Controls.Dock.Right);
        root.Children.Add(toolbar);
        root.Children.Add(side);
        root.Children.Add(_canvas);
        Content = root;
    }

    /// <summary>Gets the dialog service used for prompts (set by the workbench).</summary>
    public static Core.Documents.IDialogService? Dialogs { get; set; }

    public bool TryEdit(EditAction action)
    {
        if (_viewModel is null || action is not (EditAction.Undo or EditAction.Redo))
        {
            return false;
        }

        _ = action == EditAction.Undo ? _viewModel.UndoAsync() : _viewModel.RedoAsync();
        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.Changed -= OnChanged;
        }

        _viewModel = DataContext as DiagramDocumentViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Changed += OnChanged;
            OnChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Z && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
        {
            e.Handled = TryEdit(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? EditAction.Redo : EditAction.Undo);
        }
    }

    private static bool IsInterfaceName(string name) => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

    private static TextBlock Label(string key) => new() { Text = Loc.Get(key), Classes = { "muted" }, FontSize = 11 };

    private ToggleButton Toggle(string key, Action onClick)
    {
        var button = new ToggleButton { Content = Loc.Get(key), Padding = new Thickness(8, 2), FontSize = 12, Margin = new Thickness(0, 0, 4, 4) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button Button(string key, Func<Task> onClick)
    {
        var button = new Button { Content = Loc.Get(key), Padding = new Thickness(8, 2), FontSize = 12, Margin = new Thickness(0, 0, 4, 4) };
        button.Click += async (_, _) => await onClick().ConfigureAwait(true);
        return button;
    }

    private Task Run(Action<DiagramDocumentViewModel> action)
    {
        if (_viewModel is not null)
        {
            action(_viewModel);
        }

        return Task.CompletedTask;
    }

    private void SetTool(DiagramTool tool)
    {
        _canvas.Tool = tool;
        _selectTool.IsChecked = tool == DiagramTool.Select;
        _connectTool.IsChecked = tool == DiagramTool.Connect;
        _canvas.Cursor = tool == DiagramTool.Place ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _canvas.SetContent(_viewModel.Model, _viewModel.Layout);
        _canvas.Select(_viewModel.Selected);
        BuildProperties(_viewModel.SelectedType);
    }

    private async Task<string?> PromptAsync(string titleKey, string messageKey, string initial)
        => Dialogs is null ? null : await Dialogs.PromptAsync(Loc.Get(titleKey), Loc.Get(messageKey), initial).ConfigureAwait(true);

    private async Task PlaceAsync(double x, double y)
    {
        SetTool(DiagramTool.Select);
        if (_viewModel is not null && await PromptAsync("Diagram.New." + _placeKind, "Diagram.Prompt.TypeName", _placeKind == DiagramTypeKind.Interface ? "INew" : "New" + _placeKind).ConfigureAwait(true) is { Length: > 0 } name)
        {
            await _viewModel.CreateTypeAsync(_placeKind, name, x, y).ConfigureAwait(true);
        }
    }

    private async Task AddExistingAsync()
    {
        if (_viewModel is null || await PromptAsync("Diagram.AddExisting", "Diagram.Prompt.Filter", string.Empty).ConfigureAwait(true) is not { } filter)
        {
            return;
        }

        var matches = _viewModel.Model.Types.Where(t => !t.IsExternal && (filter.Length == 0 || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))).Select(t => t.FullName).ToList();
        _viewModel.AddExisting(matches);
    }

    private async Task ExportSvgAsync()
    {
        if (_viewModel is not null && Dialogs is not null && await Dialogs.PickSaveFileAsync("Diagram.ExportSvg", Path.GetFileNameWithoutExtension(_viewModel.Title) + ".svg").ConfigureAwait(true) is { } path)
        {
            await File.WriteAllTextAsync(path, _viewModel.ExportSvg()).ConfigureAwait(true);
        }
    }

    private async Task ExportPngAsync()
    {
        if (_viewModel is null || Dialogs is null || await Dialogs.PickSaveFileAsync("Diagram.ExportPng", Path.GetFileNameWithoutExtension(_viewModel.Title) + ".png").ConfigureAwait(true) is not { } path)
        {
            return;
        }

        var (x, y, width, height) = VsLike.Diagram.Layout.DiagramGeometry.Bounds(_canvas.Boxes);
        var export = new DiagramCanvas { Width = Math.Max(1, width), Height = Math.Max(1, height) };
        export.SetContent(_viewModel.Model, _viewModel.Layout with { Zoom = 1, OffsetX = -x, OffsetY = -y, ShowGrid = false });
        export.Measure(new Size(width, height));
        export.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2)), new Vector(192, 192));
        bitmap.Render(export);
        bitmap.Save(path);
    }

    private void BuildProperties(DiagramType? type)
    {
        _properties.Children.Clear();
        if (type is null || _viewModel is not { } vm)
        {
            _properties.Children.Add(new TextBlock { Text = Loc.Get("Diagram.NoSelection"), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            return;
        }

        _properties.Children.Add(new TextBlock { Text = type.DisplayName, FontSize = 16, FontWeight = FontWeight.SemiBold });
        _properties.Children.Add(new TextBlock { Text = type.FullName, Classes = { "muted" }, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        if (type.IsExternal)
        {
            _properties.Children.Add(new TextBlock { Text = Loc.Get("Diagram.External"), TextWrapping = TextWrapping.Wrap });
            _properties.Children.Add(Button("Diagram.RemoveFromDiagram", () => Run(v => v.RemoveFromDiagram(type.FullName))));
            return;
        }

        var name = new TextBox { Text = type.Name };
        _properties.Children.Add(Label("Diagram.Name"));
        _properties.Children.Add(new DockPanel { Children = { Button("Diagram.Rename", () => name.Text is { Length: > 0 } n && n != type.Name ? vm.RenameTypeAsync(type.FullName, n) : Task.CompletedTask), name } });

        var access = new ComboBox { ItemsSource = Enum.GetValues<Access>(), SelectedItem = type.Access, HorizontalAlignment = HorizontalAlignment.Stretch };
        var flags = Enum.GetValues<TypeModifiers>().Where(f => f != TypeModifiers.None).Select(f => new CheckBox { Content = f.ToString().ToLowerInvariant(), IsChecked = type.Modifiers.HasFlag(f), Tag = f }).ToList();
        _properties.Children.Add(Label("Diagram.Modifiers"));
        _properties.Children.Add(access);
        var flagPanel = new WrapPanel();
        flags.ForEach(f => flagPanel.Children.Add(f));
        _properties.Children.Add(flagPanel);
        _properties.Children.Add(Button("Diagram.Apply", () => vm.SetModifiersAsync(type.FullName, (Access)access.SelectedItem!, flags.Where(f => f.IsChecked == true).Aggregate(TypeModifiers.None, (all, f) => all | (TypeModifiers)f.Tag!))));

        if (type.Kind != DiagramTypeKind.Enum)
        {
            var bases = new TextBox { Text = string.Join(", ", new[] { type.BaseType }.Concat(type.InterfaceList).Where(b => b is not null)), Watermark = "Base, IFoo" };
            _properties.Children.Add(Label("Diagram.Bases"));
            _properties.Children.Add(bases);
            _properties.Children.Add(Button("Diagram.Apply", () =>
            {
                var parts = (bases.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                var baseType = parts.FirstOrDefault(p => vm.Model.Types.FirstOrDefault(t => t.DisplayName == p) is { Kind: not DiagramTypeKind.Interface } || (!IsInterfaceName(p) && type.Kind is DiagramTypeKind.Class or DiagramTypeKind.Record));
                return vm.SetBaseListAsync(type.FullName, baseType, [.. parts.Where(p => p != baseType)]);
            }));
        }

        _properties.Children.Add(Label("Diagram.Members"));
        foreach (var member in type.MemberList)
        {
            var line = new TextBlock { Text = VsLike.Diagram.Layout.BoxMetrics.Line(member), FontFamily = new FontFamily("JetBrains Mono, Menlo, monospace"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 11 };
            remove.Click += async (_, _) => await vm.RemoveMemberAsync(type.FullName, member).ConfigureAwait(true);
            var edit = new Button { Content = "✎", Padding = new Thickness(6, 0), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            edit.Click += async (_, _) =>
            {
                if (await PromptAsync("Diagram.EditMember", "Diagram.Prompt.Member", VsLike.Diagram.Layout.BoxMetrics.Line(member)).ConfigureAwait(true) is { } text
                    && MemberSpec.TryParse(member.Kind, text, type.Name, out var updated))
                {
                    await vm.UpdateMemberAsync(type.FullName, member, updated with { Modifiers = member.Modifiers, Accessors = member.Accessors, Value = updated.Value ?? member.Value }).ConfigureAwait(true);
                }
            };
            var row = new DockPanel();
            DockPanel.SetDock(remove, Avalonia.Controls.Dock.Right);
            DockPanel.SetDock(edit, Avalonia.Controls.Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(edit);
            row.Children.Add(line);
            _properties.Children.Add(row);
        }

        var kinds = type.Kind == DiagramTypeKind.Enum ? [MemberKind.EnumValue] : Enum.GetValues<MemberKind>().Where(k => k != MemberKind.EnumValue).ToArray();
        var kind = new ComboBox { ItemsSource = kinds, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var spec = new TextBox { Watermark = "+ Name(arg: int): string" };
        _properties.Children.Add(Label("Diagram.AddMember"));
        _properties.Children.Add(kind);
        _properties.Children.Add(spec);
        _properties.Children.Add(Button("Diagram.Add", async () =>
        {
            if (kind.SelectedItem is MemberKind memberKind && MemberSpec.TryParse(memberKind, spec.Text ?? string.Empty, type.Name, out var member))
            {
                await vm.AddMemberAsync(type.FullName, member).ConfigureAwait(true);
            }
            else if (Dialogs is not null)
            {
                await Dialogs.ShowMessageAsync(Loc.Get("Diagram.Title"), Loc.Get("Diagram.InvalidMember")).ConfigureAwait(true);
            }
        }));

        _properties.Children.Add(new Separator());
        _properties.Children.Add(Button("Diagram.OpenCode", () => vm.OpenInEditorAsync(type.FullName, -1)));
        _properties.Children.Add(Button("Diagram.Collapse", () => Run(v => v.ToggleCollapsed(type.FullName))));
        _properties.Children.Add(Button("Diagram.RemoveFromDiagram", () => Run(v => v.RemoveFromDiagram(type.FullName))));
        _properties.Children.Add(Button("Diagram.DeleteType", () => vm.DeleteTypeAsync(type.FullName)));
    }
}
