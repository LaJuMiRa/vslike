using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// Drawn replacements for the SF Symbols the diagram uses on macOS (SF Symbols are Apple-only). Each symbol is drawn
/// in a 16 × 16 box and scaled to the target size; names stay the SF Symbol names of the Swift code.
/// </summary>
internal static class DiagramSymbols
{
    private const double Unit = 16;

    private static readonly Dictionary<string, (string Data, bool Fill)> Paths = new()
    {
        ["cursorarrow"] = ("M4.5,2 L4.5,13.5 L7.3,10.9 L9.2,14.8 L11,14 L9.1,10.1 L12.8,10.1 Z", true),
        ["arrow.up.right.and.arrow.down.left.rectangle"] = ("M2.5,9.5 H7.5 V14 H2.5 Z M5,9.5 L11.6,4.2 M9.2,2.4 L13.8,1.8 L13.2,6.4 Z", false),
        ["plus.rectangle.on.rectangle"] = ("M5,2 H14 V10.5 H5 Z M2.5,5 V13.5 H11.5 M9.5,4 V8.5 M7.25,6.25 H11.75", false),
        ["wand.and.rays"] = ("M2.5,13.5 L10,6 M9,5 L11,7 M12.5,1.5 V3.5 M12.5,8.5 V10.5 M8,6 H6 M15,6 H13 M9.3,2.8 L10.2,3.7 M15.7,9.2 L14.8,8.3 M15.7,2.8 L14.8,3.7", false),
        ["grid"] = ("M2.5,2.5 H13.5 V13.5 H2.5 Z M6.2,2.5 V13.5 M9.8,2.5 V13.5 M2.5,6.2 H13.5 M2.5,9.8 H13.5", false),
        ["minus.magnifyingglass"] = ("M7,2.5 A4.5,4.5 0 1 1 6.99,2.5 Z M10.3,10.3 L14,14 M4.8,7 H9.2", false),
        ["plus.magnifyingglass"] = ("M7,2.5 A4.5,4.5 0 1 1 6.99,2.5 Z M10.3,10.3 L14,14 M4.8,7 H9.2 M7,4.8 V9.2", false),
        ["arrow.up.left.and.down.right.magnifyingglass"] = ("M7,2.5 A4.5,4.5 0 1 1 6.99,2.5 Z M10.3,10.3 L14,14 M5,5 L9,9 M5,7 V5 H7 M9,7 V9 H7", false),
        ["arrow.uturn.backward"] = ("M6,3.5 L2.5,7 L6,10.5 M2.5,7 H10 A3.5,3.5 0 0 1 10,14 H7.5", false),
        ["arrow.uturn.forward"] = ("M10,3.5 L13.5,7 L10,10.5 M13.5,7 H6 A3.5,3.5 0 0 0 6,14 H8.5", false),
        ["square.and.arrow.up"] = ("M5.5,6 H3.5 V14.5 H12.5 V6 H10.5 M8,10 V1.5 M5.5,4 L8,1.5 L10.5,4", false),
        ["chevron.up"] = ("M3.5,10.5 L8,6 L12.5,10.5", false),
        ["chevron.down"] = ("M3.5,6 L8,10.5 L12.5,6", false),
        ["doc.text"] = ("M3.5,1.5 H9.5 L12.5,4.5 V14.5 H3.5 Z M9.5,1.5 V4.5 H12.5 M5.5,8 H10.5 M5.5,10.5 H10.5 M5.5,13 H8.5", false),
        ["plus"] = ("M8,2.5 V13.5 M2.5,8 H13.5", false),
        ["pencil"] = ("M10.5,2.5 L13.5,5.5 L5.5,13.5 L2,14 L2.5,10.5 Z M9,4 L12,7", false),
        ["rectangle.compress.vertical"] = ("M2.5,5.5 H13.5 V10.5 H2.5 Z M8,1 V4 M6.5,2.8 L8,4 L9.5,2.8 M8,15 V12 M6.5,13.2 L8,12 L9.5,13.2", false),
        ["eye.slash"] = ("M1.5,8 C4,3.5 12,3.5 14.5,8 C12,12.5 4,12.5 1.5,8 Z M8,6 A2,2 0 1 1 7.99,6 Z M2.5,2.5 L13.5,13.5", false),
        ["trash"] = ("M2.5,4 H13.5 M6,4 V2.5 H10 V4 M4,4 L4.8,14.5 H11.2 L12,4 M6.6,6.5 V12 M9.4,6.5 V12", false),
        ["lock"] = ("M4,7.5 H12 V14 H4 Z M5.5,7.5 V5 A2.5,2.5 0 0 1 10.5,5 V7.5", false),
        ["arrow.right.circle"] = ("M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M4.5,8 H11 M8.5,5.5 L11,8 L8.5,10.5", false),
        ["minus.circle"] = ("M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M4.8,8 H11.2", false),
        ["rectangle.3.group"] = ("M1.5,2.5 H7 V7 H1.5 Z M9,2.5 H14.5 V7 H9 Z M5.25,9.5 H10.75 V14 H5.25 Z", false),
        ["arrow.up.square"] = ("M2.5,2.5 H13.5 V13.5 H2.5 Z M8,11 V5 M5.5,7.5 L8,5 L10.5,7.5", false),
        ["circle.fill"] = ("M8,3 A5,5 0 1 1 7.99,3 Z", true),
        ["square.fill"] = ("M3,3 H13 V13 H3 Z", true),
        ["bolt.fill"] = ("M9.5,1 L3,9.2 H7.4 L6.4,15 L13,6.6 H8.6 Z", true),
    };

    /// <summary>Draws a symbol into <paramref name="bounds"/>; <paramref name="background"/> is the colour cut out of filled symbols.</summary>
    public static void Draw(DrawingContext context, string name, Rect bounds, IBrush brush, IBrush? background = null)
    {
        var scale = Math.Min(bounds.Width, bounds.Height) / Unit;
        var origin = new Point(bounds.X + ((bounds.Width - (Unit * scale)) / 2), bounds.Y + ((bounds.Height - (Unit * scale)) / 2));
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            DrawUnit(context, name, brush, background ?? Brushes.White);
        }
    }

    private static void DrawUnit(DrawingContext context, string name, IBrush brush, IBrush background)
    {
        var pen = new Pen(brush, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (name.Length == 8 && name.EndsWith(".square", StringComparison.Ordinal))
        {
            context.DrawRectangle(null, pen, new Rect(2, 2, 12, 12), 3, 3);
            DrawLetter(context, name[..1], brush, 8.5);
        }
        else if (name.Length == 13 && name.EndsWith(".square.fill", StringComparison.Ordinal))
        {
            context.DrawRectangle(brush, null, new Rect(1.5, 1.5, 13, 13), 3.2, 3.2);
            DrawLetter(context, name[..1], background, 9);
        }
        else if (name == "m.circle.fill" || name == "plus.circle.fill")
        {
            context.DrawEllipse(brush, null, new Point(8, 8), 6.5, 6.5);
            if (name == "m.circle.fill")
            {
                DrawLetter(context, "m", background, 9.5);
            }
            else
            {
                context.DrawGeometry(null, new Pen(background, 1.8, lineCap: PenLineCap.Round), Geometry.Parse("M8,4.8 V11.2 M4.8,8 H11.2"));
            }
        }
        else if (name == "number")
        {
            context.DrawGeometry(null, new Pen(brush, 1.6, lineCap: PenLineCap.Round), Geometry.Parse("M6.5,2.5 L5,13.5 M11,2.5 L9.5,13.5 M3,6 H13.5 M2.5,10 H13"));
        }
        else if (name == "square.dashed")
        {
            context.DrawRectangle(null, new Pen(brush, pen.Thickness, new DashStyle([1.8, 1.6], 0)), new Rect(2.5, 2.5, 11, 11), 2, 2);
        }
        else if (Paths.TryGetValue(name, out var path))
        {
            context.DrawGeometry(path.Fill ? brush : null, path.Fill ? null : pen, Geometry.Parse(path.Data));
        }
    }

    private static void DrawLetter(DrawingContext context, string letter, IBrush brush, double size)
    {
        var text = new FormattedText(letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), size, brush);
        context.DrawText(text, new Point(8 - (text.Width / 2), 8 - (text.Height / 2)));
    }
}

/// <summary>A drawn symbol as a control (Image(systemName:) in SwiftUI); colour from <see cref="TextElement.ForegroundProperty"/>.</summary>
internal sealed class SymbolIcon : Control
{
    public static readonly StyledProperty<string> SymbolProperty = AvaloniaProperty.Register<SymbolIcon, string>(nameof(Symbol), string.Empty);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<SymbolIcon>();

    static SymbolIcon()
    {
        AffectsRender<SymbolIcon>(SymbolProperty, ForegroundProperty);
    }

    public SymbolIcon()
    {
        Width = 15;
        Height = 15;
    }

    public SymbolIcon(string symbol, double size = 15)
        : this()
    {
        Symbol = symbol;
        Width = size;
        Height = size;
    }

    public string Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context) =>
        DiagramSymbols.Draw(context, Symbol, new Rect(Bounds.Size), Foreground ?? Brushes.Gray, DiagramColors.Brush(DiagramColors.ControlBackground));
}
