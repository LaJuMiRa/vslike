using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VsLike.Diagram.Layout;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.View;

/// <summary>
/// Zeichenfläche des Klassendiagramms: Raster, Beziehungen, Typ-Boxen. Auswahl, Verschieben (mit Raster-Snap),
/// Pan (Hintergrund ziehen oder Scrollen), Zoom (⌘/Ctrl + Scrollen) und Werkzeuge zum Verbinden und Platzieren.
/// Änderungen meldet das Control nur über Ereignisse; Modell und Layout gehören dem ViewModel.
/// </summary>
public sealed class DiagramCanvas : Control
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 3;
    private readonly Dictionary<string, (double X, double Y)> _dragOverride = new(StringComparer.Ordinal);
    private DiagramModel _model = DiagramModel.Empty;
    private DiagramDocument _document = new();
    private IReadOnlyList<Box> _boxes = [];
    private string? _selected;
    private Point _pressPoint;
    private (double X, double Y) _grabOffset;
    private string? _dragging;
    private bool _panning;
    private string? _connectFrom;
    private Point _connectPoint;

    public DiagramCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public event EventHandler<string?>? SelectionChanged;

    public event EventHandler<TypePlacement>? TypeMoved;

    public event EventHandler<(string From, string To)>? ConnectRequested;

    /// <summary>Double click on a type: full name and the index of the member line under the pointer (or -1).</summary>
    public event EventHandler<(string FullName, int MemberIndex)>? OpenRequested;

    public event EventHandler<(double X, double Y)>? PlaceRequested;

    public event EventHandler<string>? DeleteRequested;

    public event EventHandler<(double Zoom, double OffsetX, double OffsetY)>? ViewChanged;

    public DiagramTool Tool { get; set; }

    public double Zoom { get; private set; } = 1;

    public Vector Offset { get; private set; }

    public string? SelectedType => _selected;

    public IReadOnlyList<Box> Boxes => _boxes;

    public void SetContent(DiagramModel model, DiagramDocument document)
    {
        _model = model;
        _document = document;
        _boxes = DiagramGeometry.Boxes(model, document);
        Zoom = Math.Clamp(document.Zoom, MinZoom, MaxZoom);
        Offset = new Vector(document.OffsetX, document.OffsetY);
        if (_selected is not null && _boxes.All(b => b.Type.FullName != _selected))
        {
            Select(null);
        }

        InvalidateVisual();
    }

    public void Select(string? fullName)
    {
        if (_selected == fullName)
        {
            return;
        }

        _selected = fullName;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, fullName);
    }

    public (double X, double Y) ToDiagram(Point point) => ((point.X - Offset.X) / Zoom, (point.Y - Offset.Y) / Zoom);

    public void SetView(double zoom, Vector offset)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Offset = offset;
        InvalidateVisual();
        ViewChanged?.Invoke(this, (Zoom, Offset.X, Offset.Y));
    }

    /// <summary>Zooms so that all boxes fit into the visible area.</summary>
    public void FitToView()
    {
        var (x, y, width, height) = DiagramGeometry.Bounds(_boxes);
        if (width <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        var zoom = Math.Clamp(Math.Min(Bounds.Width / width, Bounds.Height / height), MinZoom, 1.5);
        SetView(zoom, new Vector(-x * zoom, -y * zoom));
    }

    public override void Render(DrawingContext context)
    {
        var palette = Palette.Resolve(this);
        context.FillRectangle(palette.Background, new Rect(Bounds.Size));
        if (_document.ShowGrid)
        {
            DrawGrid(context, palette);
        }

        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Offset.X, Offset.Y)))
        {
            var boxes = CurrentBoxes();
            foreach (var (relation, from, to) in DiagramGeometry.Edges(_model, boxes))
            {
                DrawRelation(context, palette, relation, from, to);
            }

            foreach (var box in boxes)
            {
                DrawBox(context, palette, box, box.Type.FullName == _selected);
            }

            if (_connectFrom is not null && boxes.FirstOrDefault(b => b.Type.FullName == _connectFrom) is { Type: not null } source)
            {
                var (x, y) = ToDiagram(_connectPoint);
                context.DrawLine(new Pen(palette.Accent, 1.5, DashStyle.Dash), new Point(source.CenterX, source.CenterY), new Point(x, y));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        _pressPoint = point;
        var (x, y) = ToDiagram(point);
        var hit = HitTest(x, y);
        if (e.ClickCount == 2 && hit is { } opened)
        {
            var line = (int)Math.Floor((y - opened.Y - BoxMetrics.HeaderHeight) / BoxMetrics.LineHeight);
            OpenRequested?.Invoke(this, (opened.Type.FullName, line >= 0 && line < BoxMetrics.Lines(opened.Type, opened.Collapsed).Count ? line : -1));
            e.Handled = true;
            return;
        }

        if (hit is { } box)
        {
            Select(box.Type.FullName);
            if (Tool == DiagramTool.Connect && !box.Type.IsExternal)
            {
                _connectFrom = box.Type.FullName;
                _connectPoint = point;
            }
            else
            {
                _dragging = box.Type.FullName;
                _grabOffset = (x - box.X, y - box.Y);
            }
        }
        else if (Tool == DiagramTool.Place)
        {
            PlaceRequested?.Invoke(this, (BoxMetrics.Snap(x), BoxMetrics.Snap(y)));
        }
        else
        {
            Select(null);
            _panning = true;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_dragging is not null)
        {
            var (x, y) = ToDiagram(point);
            _dragOverride[_dragging] = (x - _grabOffset.X, y - _grabOffset.Y);
            InvalidateVisual();
        }
        else if (_panning)
        {
            Offset += point - _pressPoint;
            _pressPoint = point;
            InvalidateVisual();
        }
        else if (_connectFrom is not null)
        {
            _connectPoint = point;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_dragging is { } dragged && _dragOverride.TryGetValue(dragged, out var position))
        {
            _dragOverride.Clear();
            var snapped = _document.SnapToGrid ? (BoxMetrics.Snap(position.X), BoxMetrics.Snap(position.Y)) : position;
            var collapsed = _document.Find(dragged)?.Collapsed ?? false;
            TypeMoved?.Invoke(this, new TypePlacement(dragged, snapped.Item1, snapped.Item2, collapsed));
        }
        else if (_panning)
        {
            ViewChanged?.Invoke(this, (Zoom, Offset.X, Offset.Y));
        }
        else if (_connectFrom is { } from)
        {
            var (x, y) = ToDiagram(e.GetPosition(this));
            if (HitTest(x, y) is { } target && target.Type.FullName != from)
            {
                ConnectRequested?.Invoke(this, (from, target.Type.FullName));
            }
        }

        _dragging = null;
        _panning = false;
        _connectFrom = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            var point = e.GetPosition(this);
            var zoom = Math.Clamp(Zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), MinZoom, MaxZoom);
            var anchor = (point - Offset) / Zoom;
            SetView(zoom, point - (anchor * zoom));
        }
        else
        {
            SetView(Zoom, Offset + new Vector(e.Delta.X * 40, e.Delta.Y * 40));
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && _selected is { } selected)
        {
            DeleteRequested?.Invoke(this, selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Tool = DiagramTool.Select;
            Select(null);
            e.Handled = true;
        }
    }

    private static void DrawArrowHead(DrawingContext context, Point tip, Point from, IBrush fill, Pen pen, bool open)
    {
        const double length = 12;
        const double half = 6;
        var direction = tip - from;
        var norm = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        if (norm < 0.001)
        {
            return;
        }

        var unit = direction / norm;
        var normal = new Vector(-unit.Y, unit.X);
        var basePoint = tip - (unit * length);
        var left = basePoint + (normal * half);
        var right = basePoint - (normal * half);
        if (open)
        {
            context.DrawLine(pen, left, tip);
            context.DrawLine(pen, right, tip);
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tip, isFilled: true);
            g.LineTo(left);
            g.LineTo(right);
            g.EndFigure(isClosed: true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawDiamond(DrawingContext context, Point start, Point towards, IBrush fill)
    {
        var direction = towards - start;
        var norm = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        if (norm < 0.001)
        {
            return;
        }

        var unit = direction / norm;
        var normal = new Vector(-unit.Y, unit.X);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, isFilled: true);
            g.LineTo(start + (unit * 8) + (normal * 5));
            g.LineTo(start + (unit * 16));
            g.LineTo(start + (unit * 8) - (normal * 5));
            g.EndFigure(isClosed: true);
        }

        context.DrawGeometry(fill, null, geometry);
    }

    private static FormattedText Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal, bool mono = false)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(mono ? Palette.MonoFont : FontFamily.Default, style, weight), size, brush);

    private Box? HitTest(double x, double y) => CurrentBoxes().LastOrDefault(b => b.Contains(x, y)) is { Type: not null } box ? box : null;

    private IReadOnlyList<Box> CurrentBoxes()
        => _dragOverride.Count == 0 ? _boxes : [.. _boxes.Select(b => _dragOverride.TryGetValue(b.Type.FullName, out var p) ? b with { X = p.X, Y = p.Y } : b)];

    private void DrawGrid(DrawingContext context, Palette palette)
    {
        var step = BoxMetrics.GridSize * Zoom;
        if (step < 8)
        {
            return;
        }

        var pen = new Pen(palette.Grid, 1);
        for (var x = Offset.X % step; x < Bounds.Width; x += step)
        {
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        for (var y = Offset.Y % step; y < Bounds.Height; y += step)
        {
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawRelation(DrawingContext context, Palette palette, DiagramRelation relation, Box from, Box to)
    {
        var (start, end) = DiagramGeometry.Segment(from, to);
        var a = new Point(start.X, start.Y);
        var b = new Point(end.X, end.Y);
        var dashed = relation.Kind == RelationKind.Implementation;
        var pen = new Pen(palette.Line, 1.2, dashed ? new DashStyle([4, 3], 0) : null);
        context.DrawLine(pen, a, b);
        var solid = new Pen(palette.Line, 1.2);
        switch (relation.Kind)
        {
            case RelationKind.Inheritance or RelationKind.Implementation:
                DrawArrowHead(context, b, a, palette.Background, solid, open: false);
                break;
            case RelationKind.Composition:
                DrawDiamond(context, a, b, palette.Line);
                DrawArrowHead(context, b, a, palette.Line, solid, open: true);
                break;
            default:
                DrawArrowHead(context, b, a, palette.Line, solid, open: true);
                break;
        }

        if (relation.Label is { } label)
        {
            var text = Text(relation.IsMany ? label + " [*]" : label, 10, palette.Muted);
            context.DrawText(text, new Point(((a.X + b.X) / 2) + 4, ((a.Y + b.Y) / 2) - 14));
        }
    }

    private void DrawBox(DrawingContext context, Palette palette, Box box, bool selected)
    {
        var rect = new Rect(box.X, box.Y, box.Width, box.Height);
        var external = box.Type.IsExternal;
        var border = selected ? new Pen(palette.Accent, 2) : new Pen(external ? palette.Muted : palette.Line, 1);
        context.DrawRectangle(external ? palette.ExternalFill : palette.Surface, border, rect, 6, 6);
        var stereotype = BoxMetrics.Stereotype(box.Type);
        if (stereotype.Length > 0)
        {
            var s = Text(stereotype, 10, palette.Muted);
            context.DrawText(s, new Point(box.CenterX - (s.Width / 2), box.Y + 4));
        }

        var italic = box.Type.Modifiers.HasFlag(TypeModifiers.Abstract) || box.Type.Kind == DiagramTypeKind.Interface;
        var name = Text(box.Type.DisplayName, 13, external ? palette.Muted : palette.Text, FontWeight.Bold, italic ? FontStyle.Italic : FontStyle.Normal);
        context.DrawText(name, new Point(box.CenterX - (name.Width / 2), box.Y + 20));
        var lines = BoxMetrics.Lines(box.Type, box.Collapsed);
        if (lines.Count == 0)
        {
            return;
        }

        context.DrawLine(new Pen(palette.Line, 1), new Point(box.X, box.Y + BoxMetrics.HeaderHeight), new Point(box.X + box.Width, box.Y + BoxMetrics.HeaderHeight));
        for (var i = 0; i < lines.Count; i++)
        {
            var member = box.Type.MemberList[i];
            var brush = member.Modifiers.HasFlag(MemberModifiers.Static) ? palette.Accent : palette.Text;
            var text = Text(lines[i], BoxMetrics.FontSize, brush, style: member.Modifiers.HasFlag(MemberModifiers.Abstract) ? FontStyle.Italic : FontStyle.Normal, mono: true);
            context.DrawText(text, new Point(box.X + BoxMetrics.Padding, box.Y + BoxMetrics.HeaderHeight + 2 + (i * BoxMetrics.LineHeight)));
        }
    }
}
