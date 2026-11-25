using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// The floating tool bar of the diagram (DiagramToolbar in DiagramEditorView.swift). macOS draws it with the Liquid
/// Glass effect; here it is a translucent capsule with a hairline border and a soft shadow.
/// </summary>
internal sealed class DiagramToolbar : Border
{
    private readonly DiagramEditorModel _model;
    private readonly DiagramCanvas _canvas;
    private readonly List<(DiagramTool Tool, Button Button, SymbolIcon Icon)> _tools = [];
    private readonly TextBlock _zoomText;
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly SymbolIcon _gridIcon;

    public DiagramToolbar(DiagramEditorModel model, DiagramCanvas canvas, Action arrange, Func<Task> exportSvg, Func<Task> exportPng)
    {
        _model = model;
        _canvas = canvas;
        _zoomText = new TextBlock { FontSize = 11, Width = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFeatures = FontFeatureCollection.Parse("tnum") };
        _gridIcon = new SymbolIcon("grid");
        _undo = IconButton("arrow.uturn.backward", L.T("Undo (⌘Z)").Replace("⌘Z", "Ctrl+Z", StringComparison.Ordinal), () => _ = model.Undo());
        _redo = IconButton("arrow.uturn.forward", L.T("Redo (⇧⌘Z)").Replace("⇧⌘Z", "Ctrl+Shift+Z", StringComparison.Ordinal), () => _ = model.Redo());

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(ToolButton(DiagramTool.Select, "cursorarrow", L.T("Select and move")));
        row.Children.Add(Divider());
        row.Children.Add(ToolButton(DiagramTool.Create(DiagramTypeKind.Class), "c.square", L.T("New class (click on the canvas)")));
        row.Children.Add(ToolButton(DiagramTool.Create(DiagramTypeKind.Interface), "i.square", L.T("New interface")));
        row.Children.Add(ToolButton(DiagramTool.Create(DiagramTypeKind.Record), "r.square", L.T("New record")));
        row.Children.Add(ToolButton(DiagramTool.Create(DiagramTypeKind.Struct), "s.square", L.T("New struct")));
        row.Children.Add(ToolButton(DiagramTool.Create(DiagramTypeKind.Enum), "e.square", L.T("New enum")));
        row.Children.Add(ToolButton(DiagramTool.Inherit, "arrow.up.right.and.arrow.down.left.rectangle", L.T("Inherit or implement: drag from the derived type to the base type")));
        row.Children.Add(Divider());
        row.Children.Add(MenuButton("plus.rectangle.on.rectangle", L.T("Add existing types"), () => DiagramMenus.AddExistingItems(model)));
        row.Children.Add(IconButton("wand.and.rays", L.T("Arrange automatically"), arrange));
        row.Children.Add(IconButton(_gridIcon, L.T("Grid and snapping"), () => model.SetGrid(!model.Layout.ShowGrid, !model.Layout.ShowGrid)));
        row.Children.Add(Divider());
        row.Children.Add(IconButton("minus.magnifyingglass", L.T("Zoom out"), () => canvas.SetZoom(Math.Max(0.2, canvas.Zoom / 1.2))));
        row.Children.Add(_zoomText);
        row.Children.Add(IconButton("plus.magnifyingglass", L.T("Zoom in"), () => canvas.SetZoom(Math.Min(3, canvas.Zoom * 1.2))));
        row.Children.Add(IconButton("arrow.up.left.and.down.right.magnifyingglass", L.T("Fit to window"), canvas.Fit));
        row.Children.Add(Divider());
        row.Children.Add(_undo);
        row.Children.Add(_redo);
        row.Children.Add(MenuButton("square.and.arrow.up", L.T("Export"), () =>
        [
            DiagramMenus.Item(L.T("Export as SVG…"), null, () => _ = exportSvg()),
            DiagramMenus.Item(L.T("Export as PNG…"), null, () => _ = exportPng()),
        ]));

        Child = row;
        Padding = new Thickness(8, 5);
        CornerRadius = new CornerRadius(999);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        BorderThickness = new Thickness(1);
        ApplyColors();
        Update();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _model.PropertyChanged += OnModelChanged;
        _canvas.ViewChanged += OnViewChanged;
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnThemeChanged;
        }

        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        _canvas.ViewChanged -= OnViewChanged;
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnThemeChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void OnViewChanged(object? sender, EventArgs e) => _zoomText.Text = $"{(int)(_canvas.Zoom * 100)} %";

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyColors();
        Update();
    }

    private void ApplyColors()
    {
        var dark = DiagramColors.IsDark;
        Background = DiagramColors.Brush(dark ? Color.FromRgb(0x2C, 0x2E, 0x33) : Colors.White, dark ? 0.78 : 0.74);
        BorderBrush = DiagramColors.Brush(DiagramColors.Primary, dark ? 0.12 : 0.08);
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 12, Color = DiagramColors.WithOpacity(Colors.Black, dark ? 0.35 : 0.12) });
        TextElement.SetForeground(this, DiagramColors.Brush(DiagramColors.Primary, 0.85));
    }

    private void Update()
    {
        foreach (var (tool, button, icon) in _tools)
        {
            var isActive = _model.Tool == tool;
            button.Background = isActive ? DiagramColors.Brush(DiagramColors.Accent) : Brushes.Transparent;
            if (isActive)
            {
                icon.Foreground = Brushes.White;
            }
            else
            {
                icon.ClearValue(SymbolIcon.ForegroundProperty);
            }
        }

        _gridIcon.Symbol = _model.Layout.ShowGrid ? "grid" : "square.dashed";
        _undo.IsEnabled = _model.CanUndo;
        _redo.IsEnabled = _model.CanRedo;
        _undo.Opacity = _model.CanUndo ? 1 : 0.35;
        _redo.Opacity = _model.CanRedo ? 1 : 0.35;
        _zoomText.Text = $"{(int)(_canvas.Zoom * 100)} %";
    }

    private Button ToolButton(DiagramTool tool, string symbol, string help)
    {
        var icon = new SymbolIcon(symbol);
        var button = PlainButton(icon, help);
        button.CornerRadius = new CornerRadius(6);
        button.Click += (_, _) => _model.Tool = tool;
        _tools.Add((tool, button, icon));
        return button;
    }

    private static Button IconButton(string symbol, string help, Action action) => IconButton(new SymbolIcon(symbol), help, action);

    private static Button IconButton(SymbolIcon icon, string help, Action action)
    {
        var button = PlainButton(icon, help);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button MenuButton(string symbol, string help, Func<List<Control>> items)
    {
        var button = PlainButton(new SymbolIcon(symbol), help);
        button.Click += (_, _) =>
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var item in items())
            {
                flyout.Items.Add(item);
            }

            flyout.ShowAt(button);
        };
        return button;
    }

    /// <summary>A borderless 26 × 24 button (SwiftUI <c>.buttonStyle(.borderless)</c>): no chrome, only the symbol.</summary>
    private static Button PlainButton(Control content, string help)
    {
        var button = new Button
        {
            Content = content,
            Width = 26,
            Height = 24,
            Padding = default,
            Background = Brushes.Transparent,
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template = new FuncControlTemplate<Button>((owner, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                [!ContentPresenter.BackgroundProperty] = owner[!BackgroundProperty],
                [!ContentPresenter.CornerRadiusProperty] = owner[!CornerRadiusProperty],
                [!ContentPresenter.HorizontalContentAlignmentProperty] = owner[!ContentControl.HorizontalContentAlignmentProperty],
                [!ContentPresenter.VerticalContentAlignmentProperty] = owner[!ContentControl.VerticalContentAlignmentProperty],
            }),
        };
        ToolTip.SetTip(button, help);
        return button;
    }

    private static Control Divider() => new Border
    {
        Width = 1,
        Height = 18,
        Margin = new Thickness(4, 0),
        Background = DiagramColors.Brush(DiagramColors.Separator),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
