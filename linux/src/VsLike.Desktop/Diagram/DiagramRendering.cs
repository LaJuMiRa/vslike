using System.Globalization;
using Avalonia;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// Draws the parts of the class diagram (RelationsLayer, TypeBoxView, MemberLine and GridBackground in
/// DiagramEditorView.swift). Used by the canvas and by the PNG export, so both look the same.
/// </summary>
internal sealed class DiagramRendering
{
    private const double CornerRadius = 9;

    private readonly Dictionary<DiagramType, BoxText> _texts = new(ReferenceEqualityComparer.Instance);
    private bool _cachedDark;

    public DiagramRendering(FontFamily uiFont, FontFamily monoFont)
    {
        UiFont = uiFont;
        MonoFont = monoFont;
    }

    public FontFamily UiFont { get; }

    public FontFamily MonoFont { get; }

    /// <summary>Dots every grid step that move with pan and zoom.</summary>
    public static void DrawGrid(DrawingContext context, Size size, double zoom, Vector offset)
    {
        var step = BoxMetrics.GridSize * zoom * (zoom < 0.5 ? 4 : zoom < 0.9 ? 2 : 1);
        if (step <= 4)
        {
            return;
        }

        var dot = DiagramColors.Brush(DiagramColors.Primary, 0.13);
        var startX = offset.X % step;
        var startY = offset.Y % step;
        for (var x = startX; x < size.Width; x += step)
        {
            for (var y = startY; y < size.Height; y += step)
            {
                context.DrawEllipse(dot, null, new Point(x, y), 0.9, 0.9);
            }
        }
    }

    /// <summary>
    /// UML relation line: hollow triangle for inheritance (dashed for interfaces), arrow for associations, diamond for
    /// compositions, with label and multiplicity.
    /// </summary>
    public void DrawRelation(DrawingContext context, DiagramRelation relation, Point start, Point end, double scale)
    {
        var ink = DiagramColors.Brush(DiagramColors.Primary, 0.62);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var size = 12 * Math.Max(0.6, scale);
        Point At(double distance, double spread) => new(
            end.X - (distance * Math.Cos(angle)) + (spread * Math.Sin(angle)),
            end.Y - (distance * Math.Sin(angle)) - (spread * Math.Cos(angle)));

        var isHierarchy = relation.Kind is RelationKind.Inheritance or RelationKind.Implementation;
        var dash = relation.Kind == RelationKind.Implementation ? new DashStyle([6 / 1.3, 4 / 1.3], 0) : null;
        context.DrawLine(new Pen(ink, 1.3, dash), start, isHierarchy ? At(size, 0) : end);
        var pen = new Pen(ink, 1.3, lineJoin: PenLineJoin.Miter);
        if (isHierarchy)
        {
            context.DrawGeometry(DiagramColors.Brush(DiagramColors.EditorBackground), pen, Polygon(end, At(size, size * 0.55), At(size, -size * 0.55)));
            return;
        }

        context.DrawGeometry(null, pen, Polyline(At(size * 0.9, size * 0.45), end, At(size * 0.9, -size * 0.45)));
        if (relation.Kind == RelationKind.Composition)
        {
            var back = Math.Atan2(start.Y - end.Y, start.X - end.X);
            Point Diamond(double distance, double spread) => new(
                start.X - (distance * Math.Cos(back)) + (spread * Math.Sin(back)),
                start.Y - (distance * Math.Sin(back)) - (spread * Math.Cos(back)));
            context.DrawGeometry(ink, null, Polygon(start, Diamond(size * 0.8, size * 0.45), Diamond(size * 1.6, 0), Diamond(size * 0.8, -size * 0.45)));
        }

        if (relation.Label is { } label)
        {
            var text = Text(relation.IsMany ? $"{label} [*]" : label, UiFont, 10 * Math.Max(0.7, scale), DiagramColors.Brush(DiagramColors.Secondary));
            context.DrawText(text, new Point(((start.X + end.X) / 2) - (text.Width / 2), ((start.Y + end.Y) / 2) - 8 - (text.Height / 2)));
        }
    }

    /// <summary>One type in diagram coordinates: coloured header with stereotype and name, then the members in UML notation.</summary>
    public void DrawBox(DrawingContext context, DiagramBox box, bool isSelected, bool showsCollapseButton)
    {
        var type = box.Type;
        var frame = ToRect(box.Frame);
        var shape = new RoundedRect(frame, CornerRadius);
        var tint = type.IsExternal ? DiagramColors.Secondary : DiagramColors.ForKind(type.Kind);
        var body = DiagramColors.Brush(DiagramColors.ControlBackground, type.IsExternal ? 0.6 : 1);

        // Only the selected box casts a shadow: a shadow per box is an extra render pass on every zoom step.
        var shadow = isSelected ? new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 20, Color = DiagramColors.WithOpacity(Colors.Black, 0.22) }) : default;
        context.DrawRectangle(body, null, shape, shadow);
        using (context.PushGeometryClip(new RectangleGeometry(frame, CornerRadius, CornerRadius)))
        {
            var header = new Rect(frame.X, frame.Y, frame.Width, BoxMetrics.HeaderHeight);
            context.DrawRectangle(DiagramColors.Brush(tint, type.IsExternal ? 0.08 : 0.16), null, header);
            context.DrawRectangle(DiagramColors.Brush(tint), null, new Rect(frame.X, frame.Y, frame.Width, 3));
            DrawHeader(context, box, showsCollapseButton);
            if (!box.Collapsed && !type.IsExternal && type.Members.Count > 0)
            {
                context.DrawRectangle(DiagramColors.Brush(DiagramColors.Separator), null, new Rect(frame.X, header.Bottom, frame.Width, 1));
                DrawMembers(context, box);
            }
        }

        var lineWidth = isSelected ? 2.0 : 1.0;
        var border = isSelected ? DiagramColors.Brush(DiagramColors.Accent) : DiagramColors.Brush(DiagramColors.Primary, type.IsExternal ? 0.15 : 0.22);
        var dash = type.IsExternal ? new DashStyle([4, 3], 0) : null;
        context.DrawRectangle(null, new Pen(border, lineWidth, dash), new RoundedRect(frame.Deflate(lineWidth / 2), CornerRadius - (lineWidth / 2)));
    }

    /// <summary>Where the collapse chevron sits (diagram coordinates), or null when the box has none.</summary>
    public Rect? CollapseButtonFrame(DiagramBox box)
    {
        if (box.Type.IsExternal || box.Type.Members.Count == 0)
        {
            return null;
        }

        var layout = HeaderLayout(box, showsCollapseButton: true);
        return layout.Chevron;
    }

    private void DrawHeader(DrawingContext context, DiagramBox box, bool showsCollapseButton)
    {
        var layout = HeaderLayout(box, showsCollapseButton);
        var texts = TextsFor(box.Type);
        if (texts.Stereotype is { } stereotype)
        {
            context.DrawText(stereotype, layout.StereotypeOrigin);
        }

        context.DrawText(texts.Name, layout.NameOrigin);
        if (layout.Chevron is { } chevron)
        {
            DiagramSymbols.Draw(context, box.Collapsed ? "chevron.down" : "chevron.up", chevron, DiagramColors.Brush(DiagramColors.Tertiary));
        }
    }

    private (Point StereotypeOrigin, Point NameOrigin, Rect? Chevron) HeaderLayout(DiagramBox box, bool showsCollapseButton)
    {
        var frame = ToRect(box.Frame);
        var texts = TextsFor(box.Type);
        var hasChevron = showsCollapseButton && !box.Type.IsExternal && box.Type.Members.Count > 0;
        const double chevronSize = 9;
        var stereotypeHeight = texts.Stereotype?.Height ?? 0;
        var total = stereotypeHeight + (texts.Stereotype is null ? 0 : 1) + texts.Name.Height;
        var top = frame.Y + ((BoxMetrics.HeaderHeight - total) / 2);
        var stereotypeOrigin = new Point(frame.Center.X - ((texts.Stereotype?.Width ?? 0) / 2), top);
        var rowWidth = texts.Name.Width + (hasChevron ? 4 + chevronSize : 0);
        var nameY = top + total - texts.Name.Height;
        var nameX = frame.Center.X - (rowWidth / 2);
        Rect? chevron = hasChevron ? new Rect(nameX + texts.Name.Width + 4, nameY + ((texts.Name.Height - chevronSize) / 2), chevronSize, chevronSize) : null;
        return (stereotypeOrigin, new Point(nameX, nameY), chevron);
    }

    private void DrawMembers(DrawingContext context, DiagramBox box)
    {
        var texts = TextsFor(box.Type);
        var x = box.Frame.X + BoxMetrics.Padding - 2;
        var y = box.Frame.Y + BoxMetrics.HeaderHeight + (BoxMetrics.Padding / 2);
        for (var index = 0; index < box.Type.Members.Count; index++)
        {
            var member = box.Type.Members[index];
            var line = texts.Members[index];
            var symbolColor = member.Kind switch
            {
                MemberKind.Method or MemberKind.Constructor => DiagramColors.Method,
                MemberKind.EnumValue => DiagramColors.Number,
                _ => DiagramColors.Member,
            };
            DiagramSymbols.Draw(context, MemberSymbol(member.Kind), new Rect(x, y + ((BoxMetrics.LineHeight - 9) / 2), 9, 9), DiagramColors.Brush(symbolColor), DiagramColors.Brush(DiagramColors.ControlBackground));
            context.DrawText(line, new Point(x + 13, y + ((BoxMetrics.LineHeight - line.Height) / 2)));
            y += BoxMetrics.LineHeight;
        }
    }

    public static string MemberSymbol(MemberKind kind) => kind switch
    {
        MemberKind.Field => "circle.fill",
        MemberKind.Property => "square.fill",
        MemberKind.Method => "m.circle.fill",
        MemberKind.Constructor => "plus.circle.fill",
        MemberKind.Event => "bolt.fill",
        _ => "number",
    };

    private BoxText TextsFor(DiagramType type)
    {
        if (_cachedDark != DiagramColors.IsDark || _texts.Count > 2000)
        {
            _texts.Clear();
            _cachedDark = DiagramColors.IsDark;
        }

        if (_texts.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var width = BoxMetrics.Size(type, collapsed: false).Width;
        var secondary = DiagramColors.Brush(DiagramColors.Secondary);
        var primary = DiagramColors.Brush(DiagramColors.Primary);
        var stereotype = type.Stereotype.Length == 0 ? null : Text(type.Stereotype, UiFont, 9.5, secondary);
        var italic = type.Kind == DiagramTypeKind.Interface || type.Modifiers.HasFlag(TypeModifiers.Abstract);
        var name = Text(type.DisplayName, UiFont, 13, primary, FontWeight.Bold, italic ? FontStyle.Italic : FontStyle.Normal);
        name.MaxTextWidth = Math.Max(1, width - 30);
        name.Trimming = TextTrimming.CharacterEllipsis;
        var members = type.Members.Select(m => MemberText(m, width)).ToList();
        var texts = new BoxText(stereotype, name, members);
        _texts[type] = texts;
        return texts;
    }

    private FormattedText MemberText(DiagramMember member, double boxWidth)
    {
        var style = member.Modifiers.HasFlag(MemberModifiers.Abstract) ? FontStyle.Italic : FontStyle.Normal;
        var text = Text(member.UmlLine, MonoFont, BoxMetrics.FontSize - 0.5, DiagramColors.Brush(DiagramColors.Primary), FontWeight.Normal, style);
        text.MaxTextWidth = Math.Max(1, boxWidth - (2 * BoxMetrics.Padding) - 9);
        text.Trimming = TextTrimming.CharacterEllipsis;
        if (member.Modifiers.HasFlag(MemberModifiers.Static))
        {
            text.SetTextDecorations(TextDecorations.Underline);
        }

        return text;
    }

    internal static FormattedText Text(string value, FontFamily family, double size, IBrush brush, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family, style, weight), size, brush);

    internal static Rect ToRect(CGRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static StreamGeometry Polygon(params Point[] points) => Shape(points, closed: true);

    private static StreamGeometry Polyline(params Point[] points) => Shape(points, closed: false);

    private static StreamGeometry Shape(Point[] points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(points[0], closed);
        foreach (var point in points.Skip(1))
        {
            context.LineTo(point);
        }

        context.EndFigure(closed);
        return geometry;
    }

    private sealed record BoxText(FormattedText? Stereotype, FormattedText Name, IReadOnlyList<FormattedText> Members);
}
