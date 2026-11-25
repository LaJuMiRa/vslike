using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// The graphical class diagram (DiagramEditorView.swift): grid, relations and type boxes on a zoomable, pannable
/// canvas with a floating tool bar. Selecting a box shows it in the inspector.
/// </summary>
public sealed class DiagramEditorView : UserControl
{
    private readonly DiagramCanvas _canvas;
    private readonly Control _loading;
    private readonly Panel _empty;
    private readonly Button _addAllButton;

    public DiagramEditorView(DiagramEditorModel model)
    {
        Model = model;
        _canvas = new DiagramCanvas(model);
        _canvas.BoxMenuRequested += (_, box) => ShowMenu(DiagramMenus.TypeMenu(model, box.Type, box.Collapsed));
        _canvas.CanvasMenuRequested += (_, point) => ShowMenu(DiagramMenus.CanvasMenu(model, kind => PromptNewType(kind, point), ArrangeAll, _canvas.Fit));
        _canvas.CreateRequested += (_, request) => PromptNewType(request.Kind, request.Point);
        _loading = LoadingView();
        (_empty, _addAllButton) = EmptyView(model);
        var toolbar = new DiagramToolbar(model, _canvas, ArrangeAll, ExportSvg, ExportPng) { Margin = new Thickness(12) };
        Content = new Panel { Children = { _canvas, _loading, _empty, toolbar }, ClipToBounds = true };
        Update();
    }

    public DiagramEditorModel Model { get; }

    /// <summary>Fits the diagram into the window (also used by menus and the command palette).</summary>
    public void Fit() => _canvas.Fit();

    /// <summary>Arranges all boxes, animated.</summary>
    public void ArrangeAll()
    {
        _canvas.AnimateNextLayoutChange();
        Model.ArrangeAll();
    }

    public async Task ExportSvg()
    {
        if (await PickFile(Model.Name + ".svg", "SVG", "*.svg", "image/svg+xml") is not { } file)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(Model.ExportSVG());
    }

    public async Task ExportPng()
    {
        if (await PickFile(Model.Name + ".png", "PNG", "*.png", "image/png") is not { } file)
        {
            return;
        }

        using var bitmap = DiagramSnapshot.Render(Model.Model, Model.Layout, GetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty));
        await using var stream = await file.OpenWriteAsync();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Model.PropertyChanged += OnModelChanged;
        Update();
        _canvas.Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Model.PropertyChanged -= OnModelChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagramEditorModel.Selection) && Model.Selection is not null)
        {
            Model.Workspace.IsInspectorVisible = true;
        }

        if (e.PropertyName is nameof(DiagramEditorModel.IsLoading) or nameof(DiagramEditorModel.Boxes) or nameof(DiagramEditorModel.Model)
            or nameof(DiagramEditorModel.MissingTypes))
        {
            Update();
        }
    }

    private void Update()
    {
        _loading.IsVisible = Model.IsLoading;
        _empty.IsVisible = !Model.IsLoading && Model.Boxes.Count == 0;
        var missing = Model.MissingTypes.Count;
        _addAllButton.IsVisible = missing > 0;
        _addAllButton.Content = L.T("Add All %lld Types", missing);
    }

    private void PromptNewType(DiagramTypeKind kind, CGPoint point) => DiagramMenus.PromptNewType(Model.Workspace, Model, kind, point);

    private void ShowMenu(List<Control> items)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        menu.Open(_canvas);
    }

    private async Task<IStorageFile?> PickFile(string name, string typeName, string pattern, string mime)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return null;
        }

        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = name,
            DefaultExtension = Path.GetExtension(name).TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern], MimeTypes = [mime] }],
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(Model.Folder),
        });
    }

    private static Control LoadingView()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120 });
        panel.Children.Add(new TextBlock { Text = L.T("Reading types…"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = DiagramColors.Brush(DiagramColors.Secondary) });
        return panel;
    }

    /// <summary>ContentUnavailableView "Empty diagram" with the button that adds all source types.</summary>
    private static (Panel Panel, Button AddAll) EmptyView(DiagramEditorModel model)
    {
        var addAll = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = DiagramColors.Brush(DiagramColors.Accent),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 8, 0, 0),
        };
        addAll.Click += (_, _) => model.AddExisting(model.MissingTypes.Select(t => t.FullName));
        var panel = new StackPanel { Spacing = 6, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new SymbolIcon("rectangle.3.group", 44) { Foreground = DiagramColors.Brush(DiagramColors.Secondary), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = L.T("Empty diagram"), FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Pick a type tool above and click on the canvas, or add the existing types."),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = DiagramColors.Brush(DiagramColors.Secondary),
        });
        panel.Children.Add(addAll);
        return (panel, addAll);
    }
}

/// <summary>Static rendering of the whole diagram for the PNG export (DiagramSnapshot in DiagramEditorView.swift), at 2× scale.</summary>
internal static class DiagramSnapshot
{
    public static RenderTargetBitmap Render(DiagramModel model, DiagramDocument layout, FontFamily uiFont)
    {
        var boxes = DiagramGeometry.Boxes(model, layout);
        var bounds = DiagramGeometry.Bounds(boxes, margin: 30);
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(bounds.Width * 2)), Math.Max(1, (int)Math.Ceiling(bounds.Height * 2)));
        var bitmap = new RenderTargetBitmap(size, new Vector(192, 192));
        var rendering = new DiagramRendering(uiFont, new FontFamily(Preferences.EditorFontFamily));
        using var context = bitmap.CreateDrawingContext();
        context.DrawRectangle(DiagramColors.Brush(DiagramColors.EditorBackground), null, new Rect(0, 0, bounds.Width, bounds.Height));
        foreach (var edge in DiagramGeometry.Edges(model, boxes))
        {
            var start = new Point(edge.Start.X - bounds.MinX, edge.Start.Y - bounds.MinY);
            var end = new Point(edge.End.X - bounds.MinX, edge.End.Y - bounds.MinY);
            rendering.DrawRelation(context, edge.Relation, start, end, 1);
        }

        using (context.PushTransform(Matrix.CreateTranslation(-bounds.MinX, -bounds.MinY)))
        {
            foreach (var box in boxes)
            {
                rendering.DrawBox(context, box, isSelected: false, showsCollapseButton: false);
            }
        }

        return bitmap;
    }
}
