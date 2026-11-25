using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// The zoomable, pannable drawing surface of the class diagram: grid, relations, type boxes and the line drawn by the
/// inherit tool (the ZStack of DiagramEditorView.swift with its gestures and ScrollMonitor). Redraws only when the
/// model, the view (zoom, offset) or a drag changes.
/// </summary>
internal sealed partial class DiagramCanvas : Control
{
    private const double DragThreshold = 3;

    /// <summary>Pixels per wheel step when panning (macOS: precise touchpad deltas, or 10 pt per accelerated line).</summary>
    private const double WheelPanStep = 30;

    private readonly DiagramEditorModel _model;
    private readonly Dictionary<string, Vector> _dragOffsets = [];
    private DiagramRendering? _rendering;
    private Point? _pressPoint;
    private DiagramBox? _pressBox;
    private bool _isDragging;
    private Vector _panStart;
    private (string From, Point To)? _connection;
    private double _lastPinchScale = 1;

    public DiagramCanvas(DiagramEditorModel model)
    {
        _model = model;
        Focusable = true;
        ClipToBounds = true;
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, (_, _) => _lastPinchScale = 1);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);
        AddHandler(ContextRequestedEvent, OnContextMenuRequested);
    }

    /// <summary>Raised after zoom or offset changed (the tool bar shows the zoom).</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Right click on a box: the view shows the type menu.</summary>
    public event EventHandler<DiagramBox>? BoxMenuRequested;

    /// <summary>Right click on the free canvas: the view shows the canvas menu (argument: diagram coordinates).</summary>
    public event EventHandler<CGPoint>? CanvasMenuRequested;

    /// <summary>Click on the canvas with a create tool (argument: diagram coordinates).</summary>
    public event EventHandler<(DiagramTypeKind Kind, CGPoint Point)>? CreateRequested;

    public double Zoom { get; private set; } = 1;

    public Vector Offset { get; private set; }

    public bool DidRestoreView { get; private set; }

    public CGPoint DiagramPoint(Point point) => new((point.X - Offset.X) / Zoom, (point.Y - Offset.Y) / Zoom);

    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.2, 3);
        PersistView();
    }

    public void ZoomBy(double factor, Point location)
    {
        var newZoom = Math.Min(3, Math.Max(0.2, Zoom * factor));
        var anchor = DiagramPoint(location);
        Zoom = newZoom;
        Offset = new Vector(location.X - (anchor.X * newZoom), location.Y - (anchor.Y * newZoom));
        PersistView();
    }

    /// <summary>Fits all boxes into the view, leaving room for the floating tool bar at the top.</summary>
    public void Fit()
    {
        var size = Bounds.Size;
        if (_model.Boxes.Count == 0 || size.Width <= 0)
        {
            return;
        }

        var bounds = DiagramGeometry.Bounds(_model.Boxes, margin: 40);
        var scale = Math.Min(1.25, Math.Max(0.2, Math.Min(size.Width / bounds.Width, (size.Height - 60) / bounds.Height)));
        var offset = new Vector(
            ((size.Width - (bounds.Width * scale)) / 2) - (bounds.MinX * scale),
            60 + ((size.Height - 60 - (bounds.Height * scale)) / 2) - (bounds.MinY * scale));
        AnimateView(scale, offset);
        _model.SetView(scale, new CGPoint(offset.X, offset.Y));
    }

    /// <summary>Restores zoom and offset from the diagram file once, or fits the diagram shortly after opening.</summary>
    public void RestoreView()
    {
        if (DidRestoreView)
        {
            return;
        }

        DidRestoreView = true;
        var layout = _model.Layout;
        if (layout.OffsetX != 0 || layout.OffsetY != 0 || layout.Zoom != 1)
        {
            Zoom = layout.Zoom;
            Offset = new Vector(layout.OffsetX, layout.OffsetY);
            OnViewChanged();
        }
        else
        {
            Offset = new Vector(40, 70);
            OnViewChanged();
            DispatcherTimer.RunOnce(Fit, TimeSpan.FromSeconds(0.6));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var uiFont = GetValue(TextElement.FontFamilyProperty);
        _rendering = new DiagramRendering(uiFont, new FontFamily(Preferences.EditorFontFamily));
        _model.PropertyChanged += OnModelChanged;
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnThemeChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.NewSize.Width > 0)
        {
            RestoreView();
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.DrawRectangle(DiagramColors.Brush(DiagramColors.EditorBackground), null, new Rect(size));
        if (_rendering is null)
        {
            return;
        }

        if (_model.Layout.ShowGrid)
        {
            DiagramRendering.DrawGrid(context, size, Zoom, Offset);
        }

        var boxes = DisplayedBoxes();
        var edges = ReferenceEquals(boxes, _model.Boxes) ? _model.Edges : DiagramGeometry.Edges(_model.Model, boxes);
        foreach (var edge in edges)
        {
            _rendering.DrawRelation(context, edge.Relation, ToScreen(edge.Start), ToScreen(edge.End), Zoom);
        }

        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Offset.X, Offset.Y)))
        {
            foreach (var box in boxes)
            {
                _rendering.DrawBox(context, box, _model.Selection == box.Type.FullName, showsCollapseButton: true);
            }
        }

        if (_connection is { } connection && boxes.FirstOrDefault(b => b.Type.FullName == connection.From) is { } source)
        {
            var pen = new Pen(DiagramColors.Brush(DiagramColors.Accent), 2, new DashStyle([3, 2], 0));
            context.DrawLine(pen, ToScreen(source.Center), connection.To);
        }
    }

    /// <summary>Boxes at their current positions: moved by an ongoing drag or by the arrange animation.</summary>
    private IReadOnlyList<DiagramBox> DisplayedBoxes()
    {
        var boxes = AnimatedBoxes() ?? _model.Boxes;
        if (_dragOffsets.Count == 0)
        {
            return boxes;
        }

        return [.. boxes.Select(box => _dragOffsets.TryGetValue(box.Type.FullName, out var shift) ? MovedTo(box, box.Frame.X + (shift.X / Zoom), box.Frame.Y + (shift.Y / Zoom)) : box)];
    }

    private static DiagramBox MovedTo(DiagramBox box, double x, double y) => box with { Frame = box.Frame with { X = x, Y = y } };

    private Point ToScreen(CGPoint point) => new((point.X * Zoom) + Offset.X, (point.Y * Zoom) + Offset.Y);

    private DiagramBox? HitBox(Point point)
    {
        var target = DiagramPoint(point);
        return DisplayedBoxes().LastOrDefault(b => b.Frame.Contains(target));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagramEditorModel.Boxes))
        {
            OnBoxesChanged();
        }

        if (e.PropertyName is nameof(DiagramEditorModel.Boxes) or nameof(DiagramEditorModel.Edges) or nameof(DiagramEditorModel.Selection)
            or nameof(DiagramEditorModel.Layout) or nameof(DiagramEditorModel.Tool))
        {
            InvalidateVisual();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void PersistView()
    {
        _model.SetView(Zoom, new CGPoint(Offset.X, Offset.Y));
        OnViewChanged();
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
