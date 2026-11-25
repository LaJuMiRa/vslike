using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>
/// Vector symbols drawn for VsLike in the style of the SF Symbols the macOS app uses (no Linux original). Names are the
/// SF Symbols names the models and views use (<see cref="FileNode.Symbol"/>, <see cref="EditorTab.Symbol"/>), so the
/// mapping stays one to one. All paths are drawn on a 24 × 24 grid.
/// </summary>
public static class Icons
{
    // Layer modes: S = stroked outline, F = filled, K = knockout (filled with the background, for the symbol inside
    // a filled shape), KS = knockout stroke.
    private const string Circle = "M12 2.5a9.5 9.5 0 1 0 0 19 9.5 9.5 0 0 0 0-19z";
    private const string Folder = "M3 6.5A1.5 1.5 0 0 1 4.5 5h4.6l2 2h8.4A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z";
    private const string Doc = "M6.5 3h7.5l4.5 4.5v12A1.5 1.5 0 0 1 17 21H6.5A1.5 1.5 0 0 1 5 19.5v-15A1.5 1.5 0 0 1 6.5 3zM14 3v4.5h4.5";
    private const string Seal = "M12 2l2.4 1.8 3-.2.9 2.8 2.5 1.7-.9 2.9.9 2.9-2.5 1.7-.9 2.8-3-.2L12 22l-2.4-1.8-3 .2-.9-2.8-2.5-1.7.9-2.9-.9-2.9 2.5-1.7.9-2.8 3 .2z";
    private const string Box = "M12 2.5l8.5 4.5v10L12 21.5 3.5 17V7z";
    private const string Gear = "M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1M12 6a6 6 0 1 0 0 12 6 6 0 0 0 0-12zM12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z";

    private static readonly Dictionary<string, (string Mode, string Data)[]> Symbols = new()
    {
        ["play.fill"] = [("F", "M7 4.6v14.8a1 1 0 0 0 1.5.86l12.1-7.4a1 1 0 0 0 0-1.72L8.5 3.74A1 1 0 0 0 7 4.6z")],
        ["play"] = [("S", "M7 4.6v14.8a1 1 0 0 0 1.5.86l12.1-7.4a1 1 0 0 0 0-1.72L8.5 3.74A1 1 0 0 0 7 4.6z")],
        ["stop.fill"] = [("F", "M7.5 5h9A2.5 2.5 0 0 1 19 7.5v9a2.5 2.5 0 0 1-2.5 2.5h-9A2.5 2.5 0 0 1 5 16.5v-9A2.5 2.5 0 0 1 7.5 5z")],
        ["pause.fill"] = [("F", "M6.5 4.5h3.5v15H6.5zM14 4.5h3.5v15H14z")],
        ["hammer"] = [("S", "M10 4.5h6.5l3 3-2 2-2.5-2.5h-1.6M13.2 8.3L4.3 17.2a1.5 1.5 0 0 0 2.1 2.1l8.9-8.9")],
        ["chevron.down"] = [("S", "M6 9l6 6 6-6")],
        ["chevron.right"] = [("S", "M9 5.5l6.5 6.5L9 18.5")],
        ["chevron.compact.right"] = [("S", "M10 6.5l4 5.5-4 5.5")],
        ["chevron.left.forwardslash.chevron.right"] = [("S", "M7.5 7L2.5 12l5 5M16.5 7l5 5-5 5M14 4l-4 16")],
        ["arrow.turn.down.right"] = [("S", "M5 4v7a3 3 0 0 0 3 3h11M15 10l4 4-4 4")],
        ["arrow.down.to.line"] = [("S", "M12 3v12M7 10l5 5 5-5M5 20h14")],
        ["arrow.up.to.line"] = [("S", "M12 21V9M7 14l5-5 5 5M5 4h14")],
        ["arrow.triangle.2.circlepath"] = [("S", "M4 12a8 8 0 0 1 13.7-5.6L20 9M20 4v5h-5M20 12a8 8 0 0 1-13.7 5.6L4 15M4 20v-5h5")],
        ["arrow.clockwise"] = [("S", "M20 12a8 8 0 1 1-2.3-5.7L20 8.5M20 3.5v5h-5")],
        ["arrow.2.squarepath"] = [("S", "M4 11V8a2 2 0 0 1 2-2h12M15 3l3 3-3 3M20 13v3a2 2 0 0 1-2 2H6M9 21l-3-3 3-3")],
        ["arrow.right.circle"] = [("S", Circle + "M7.5 12h9M13 8.5l3.5 3.5-3.5 3.5")],
        ["arrow.triangle.branch"] = [("S", "M7 3v18M7 8c0 5 10 3 10 9v4M13.5 17.5L17 21l3.5-3.5")],
        ["trash"] = [("S", "M4 6.5h16M9 6.5V4h6v2.5M6 6.5L7 20h10l1-13.5M10 10.5v6M14 10.5v6")],
        ["play.circle"] = [("S", Circle), ("F", "M10 8.3v7.4l6-3.7z")],
        ["play.circle.fill"] = [("F", Circle), ("K", "M10 8.3v7.4l6-3.7z")],
        ["checkmark"] = [("S", "M5 12.5l4.5 4.5L19 7.5")],
        ["checkmark.circle.fill"] = [("F", Circle), ("KS", "M7.5 12.3l3 3 6-6.3")],
        ["checkmark.diamond"] = [("S", "M12 2.5l9.5 9.5-9.5 9.5L2.5 12zM8.3 12l2.7 2.7 4.8-5.2")],
        ["checkmark.seal"] = [("S", Seal + "M8.3 12.2l2.5 2.5 4.9-5.2")],
        ["checkmark.seal.fill"] = [("F", Seal), ("KS", "M8.3 12.2l2.5 2.5 4.9-5.2")],
        ["xmark"] = [("S", "M6 6l12 12M18 6L6 18")],
        ["xmark.octagon.fill"] = [("F", "M8.3 2.5h7.4l5.8 5.8v7.4l-5.8 5.8H8.3l-5.8-5.8V8.3z"), ("KS", "M9 9l6 6M15 9l-6 6")],
        ["xmark.circle.fill"] = [("F", Circle), ("KS", "M9 9l6 6M15 9l-6 6")],
        ["exclamationmark.triangle.fill"] = [
            ("F", "M12 2.8a1.9 1.9 0 0 1 1.65.95l8.4 14.6A1.9 1.9 0 0 1 20.4 21.2H3.6a1.9 1.9 0 0 1-1.65-2.85l8.4-14.6A1.9 1.9 0 0 1 12 2.8z"),
            ("KS", "M12 9v5"),
            ("K", "M12 16.4a1.15 1.15 0 1 0 0 2.3 1.15 1.15 0 0 0 0-2.3z"),
        ],
        ["exclamationmark.bubble"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v10a1.5 1.5 0 0 1-1.5 1.5H9.5L5 21v-3.5h-.5A1.5 1.5 0 0 1 3 16V6a1.5 1.5 0 0 1 1.5-1.5zM12 8v4.5M12 14.8v.2")],
        ["info.circle.fill"] = [("F", Circle), ("KS", "M12 11v6"), ("K", "M12 6.6a1.2 1.2 0 1 0 0 2.4 1.2 1.2 0 0 0 0-2.4z")],
        ["minus.circle"] = [("S", Circle + "M8 12h8")],
        ["minus.circle.fill"] = [("F", Circle), ("KS", "M8 12h8")],
        ["circle"] = [("S", "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z")],
        ["circle.fill"] = [("F", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z")],
        ["circle.dotted"] = [("D", "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z")],
        ["shippingbox"] = [("S", Box + "M3.5 7L12 11.5 20.5 7M12 11.5v10M7.8 4.7l8.4 4.6")],
        ["shippingbox.fill"] = [("F", Box), ("KS", "M3.5 7L12 11.5 20.5 7M12 11.5v10M7.8 4.7l8.4 4.6")],
        ["rectangle.3.group"] = [("S", "M4 4h6v6H4zM14 4h6v6h-6zM9 14h6v6H9z")],
        ["terminal"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5zM7 9l3 3-3 3M12.5 15H17")],
        ["rectangle.bottomthird.inset.filled"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5z"), ("F", "M5.5 13.5h13v3.5h-13z")],
        ["sidebar.left"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5zM9.5 4.5v15M5.5 8h2M5.5 11h2")],
        ["sidebar.right"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5zM14.5 4.5v15M16.5 8h2M16.5 11h2")],
        ["folder"] = [("S", Folder)],
        ["folder.fill"] = [("F", Folder)],
        ["folder.badge.plus"] = [("S", Folder + "M16 10.5v6M13 13.5h6")],
        ["folder.fill.badge.gearshape"] = [("F", Folder), ("KS", "M15.5 10.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z")],
        ["square.stack.3d.up"] = [("S", "M12 3l9 4.5-9 4.5-9-4.5zM3 12l9 4.5 9-4.5M3 16.5L12 21l9-4.5")],
        ["square.stack.3d.up.fill"] = [("F", "M12 3l9 4.5-9 4.5-9-4.5z"), ("S", "M3 12l9 4.5 9-4.5M3 16.5L12 21l9-4.5")],
        ["plus"] = [("S", "M12 5v14M5 12h14")],
        ["plus.square.on.square"] = [("S", "M8 3.5h10.5A2 2 0 0 1 20.5 5.5V16M5 7.5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-10a2 2 0 0 1 2-2zM10 11v7M6.5 14.5h7")],
        ["command"] = [("S", "M9 9V6.5A2.5 2.5 0 1 0 6.5 9h11A2.5 2.5 0 1 0 15 6.5v11a2.5 2.5 0 1 0 2.5-2.5h-11A2.5 2.5 0 1 0 9 17.5z")],
        ["doc"] = [("S", Doc)],
        ["doc.text"] = [("S", Doc + "M8.5 12h7M8.5 15h7M8.5 18h4.5")],
        ["doc.richtext"] = [("S", Doc + "M8.5 12h7M8.5 15h7M8.5 18h4.5")],
        ["doc.badge.plus"] = [("S", Doc + "M11.75 11v7M8.25 14.5h7")],
        ["doc.on.doc"] = [("S", "M10 7h7l3 3v10.5a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-12.5a1 1 0 0 1 1-1zM6 17H5a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h7l2 2")],
        ["doc.text.magnifyingglass"] = [("S", Doc + "M11 11.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM13.2 16.7l2.3 2.3")],
        ["doc.text.fill"] = [("F", Doc)],
        ["text.alignleft"] = [("S", "M4 6h16M4 10h10M4 14h16M4 18h10")],
        ["text.badge.minus"] = [("S", "M4 6h11M4 10h11M4 14h7M15 17.5h6")],
        ["text.word.spacing"] = [("S", "M3 7v10M21 7v10M7 12h3.5M13.5 12H17")],
        ["textformat"] = [("S", "M3 18L8 6l5 12M5 14h6M15.5 11.5a2.5 2.5 0 0 1 5 0V18M20.5 14.5h-2.5a2 2 0 1 0 0 3.5h2.5")],
        ["asterisk"] = [("S", "M12 4v16M5.1 8l13.8 8M18.9 8L5.1 16")],
        ["square.and.arrow.down.on.square"] = [("S", "M8.5 9H6.5a1 1 0 0 0-1 1v9.5a1 1 0 0 0 1 1h11a1 1 0 0 0 1-1V10a1 1 0 0 0-1-1h-2M12 3v11M8.5 10.5L12 14l3.5-3.5")],
        ["gearshape"] = [("S", Gear)],
        ["gearshape.2"] = [("S", Gear)],
        ["lightbulb"] = [("S", "M9 18h6M10 21h4M12 3a6 6 0 0 0-3.5 10.9V16h7v-2.1A6 6 0 0 0 12 3z")],
        ["wand.and.stars"] = [("S", "M4 20L15 9M13 7l4 4M18 3v4M16 5h4M8 4v3M6.5 5.5h3M19 14v3M17.5 15.5h3")],
        ["pencil"] = [("S", "M4 20l1-4.5L16 4.5l3.5 3.5L8.5 19zM14 6.5l3.5 3.5")],
        ["magnifyingglass"] = [("S", "M10.5 4a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM15.3 15.3L20 20")],
        ["number"] = [("S", "M9.5 4L7.5 20M16.5 4l-2 16M4.5 9h15.5M4 15h15.5")],
        ["ladybug"] = [("S", "M12 7.5c3.3 0 6 2.9 6 6.5s-2.7 6.5-6 6.5-6-2.9-6-6.5 2.7-6.5 6-6.5zM12 7.5v13M8 4l2 3.5M16 4l-2 3.5M3.5 13H6M18 13h2.5M4.5 18.5l2-1.3M19.5 18.5l-2-1.3")],
        ["cube.box"] = [("S", "M12 3l8 4v10l-8 4-8-4V7zM4 7l8 4 8-4M12 11v10")],
        ["cube.box.fill"] = [("F", "M12 3l8 4v10l-8 4-8-4V7z"), ("KS", "M4 7l8 4 8-4M12 11v10")],
        ["slider.horizontal.3"] = [("S", "M4 6h16M4 12h16M4 18h16"), ("F", "M8 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM15 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM10 16a2 2 0 1 0 0 4 2 2 0 0 0 0-4z")],
        ["globe"] = [("S", Circle + "M2.5 12h19M12 2.5c3.2 3.6 3.2 15.4 0 19M12 2.5c-3.2 3.6-3.2 15.4 0 19")],
        ["books.vertical"] = [("S", "M4 4h4v16H4zM10 4h4v16h-4zM15.8 5.2l3.8-1 3.2 15.4-3.8 1z")],
        ["network"] = [("S", "M12 3.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM5 16.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM19 16.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM12 7.5V12M12 12l-5.5 4.8M12 12l5.5 4.8")],
        ["character.cursor.ibeam"] = [("S", "M3.5 18l4-10.5 4 10.5M5 14.5h5M17 5v14M15 5h4M15 19h4")],
        ["arrowtriangle.right.fill"] = [("F", "M8 5l10 7-10 7z")],
        ["checklist"] = [("S", "M4 6l1.5 1.5L8.5 4.5M4 12l1.5 1.5 3-3M4 18l1.5 1.5 3-3M11.5 6.5H20M11.5 12.5H20M11.5 18.5H20")],
        ["link"] = [("S", "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1")],
        ["c.square"] = [("S", "M6 3.5h12A2.5 2.5 0 0 1 20.5 6v12a2.5 2.5 0 0 1-2.5 2.5H6A2.5 2.5 0 0 1 3.5 18V6A2.5 2.5 0 0 1 6 3.5zM15 9.3a4 4 0 1 0 0 5.4")],
        ["curlybraces"] = [("S", "M9 4H8a2 2 0 0 0-2 2v3.5L4 12l2 2.5V18a2 2 0 0 0 2 2h1M15 4h1a2 2 0 0 1 2 2v3.5l2 2.5-2 2.5V18a2 2 0 0 1-2 2h-1")],
        ["photo"] = [("S", "M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5zM3 16l5-5 4 4 3-3 6 6M15.5 7.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z")],
        ["line.3.horizontal"] = [("S", "M4 7h16M4 12h16M4 17h16")],
        ["minus"] = [("S", "M6 12h12")],
        ["square"] = [("S", "M6.5 6.5h11v11h-11z")],
        ["square.on.square"] = [("S", "M8.5 8.5h10v10h-10zM5.5 15.5v-10h10")],
    };

    /// <summary>Whether a symbol is known (unknown names fall back to <c>doc</c>).</summary>
    public static bool Has(string name) => Symbols.ContainsKey(name);

    /// <summary>A symbol of <paramref name="size"/> pixels; <paramref name="brushKey"/> is a theme resource key.</summary>
    public static Control Make(string name, double size = 14, string brushKey = "TextSecondary", double weight = 1.8)
    {
        var icon = Make(name, size, (IBrush?)null, weight);
        Ui.Res(icon, SymbolIcon.ForegroundProperty, brushKey);
        return icon;
    }

    /// <summary>A symbol drawn with a fixed brush (for example white on an accent background).</summary>
    public static SymbolIcon Make(string name, double size, IBrush? brush, double weight = 1.8) =>
        new(name, weight) { Width = size, Height = size, Foreground = brush };

    /// <summary>The layers of a symbol: mode (S, F, K, KS, D) and path data.</summary>
    internal static IReadOnlyList<(string Mode, string Data)> Layers(string name) =>
        Symbols.TryGetValue(name, out var layers) ? layers : Symbols["doc"];
}

/// <summary>Draws one symbol from <see cref="Icons"/> in its <see cref="Foreground"/>.</summary>
public sealed class SymbolIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SymbolIcon, IBrush?>(nameof(Foreground), inherits: true);

    private static readonly Dictionary<string, Geometry> Cache = [];
    private readonly IReadOnlyList<(string Mode, Geometry Geometry)> _layers;
    private readonly double _weight;

    static SymbolIcon()
    {
        AffectsRender<SymbolIcon>(ForegroundProperty);
    }

    public SymbolIcon(string symbol, double weight = 1.8)
    {
        Symbol = symbol;
        _weight = weight;
        _layers = [.. Icons.Layers(symbol).Select(l => (l.Mode, Parse(l.Data)))];
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public string Symbol { get; }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0)
        {
            return;
        }

        var knockout = this.TryFindResource("IconKnockout", ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.White;
        var offset = new Point((Bounds.Width - (24 * scale)) / 2, (Bounds.Height - (24 * scale)) / 2);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));
        foreach (var (mode, geometry) in _layers)
        {
            switch (mode)
            {
                case "F":
                    context.DrawGeometry(Foreground, null, geometry);
                    break;
                case "K":
                    context.DrawGeometry(knockout, null, geometry);
                    break;
                case "S":
                    context.DrawGeometry(null, Pen(Foreground, _weight), geometry);
                    break;
                case "KS":
                    context.DrawGeometry(null, Pen(knockout, _weight), geometry);
                    break;
                case "D":
                    context.DrawGeometry(null, new Pen(Foreground, _weight + 0.4, new DashStyle([0.01, 2.2 / (_weight + 0.4)], 0), PenLineCap.Round), geometry);
                    break;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? 14 : Width, double.IsNaN(Height) ? 14 : Height);

    private static Pen Pen(IBrush? brush, double weight) =>
        new(brush, weight, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static Geometry Parse(string data)
    {
        if (!Cache.TryGetValue(data, out var geometry))
        {
            geometry = Geometry.Parse(data);
            Cache[data] = geometry;
        }

        return geometry;
    }
}
