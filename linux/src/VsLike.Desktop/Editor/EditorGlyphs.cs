using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace VsLike.Desktop.Editor;

/// <summary>
/// Small vector symbols for the editor (no Swift original): Linux has no SF Symbols, so the editor draws the few it
/// uses (diagnostic badges, quick action and context menu icons, completion kinds) from its own path data in a 16×16 box.
/// </summary>
public static class EditorGlyphs
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["xmark.octagon.fill"] = "M5.3,1 L10.7,1 L15,5.3 L15,10.7 L10.7,15 L5.3,15 L1,10.7 L1,5.3 Z M5.2,4 L4,5.2 L6.8,8 L4,10.8 L5.2,12 L8,9.2 L10.8,12 L12,10.8 L9.2,8 L12,5.2 L10.8,4 L8,6.8 Z",
        ["exclamationmark.triangle.fill"] = "M8,1 L15.5,14.5 L0.5,14.5 Z M7.1,5.5 L7.4,10.5 L8.6,10.5 L8.9,5.5 Z M7,11.6 L7,13.2 L9,13.2 L9,11.6 Z",
        ["info.circle.fill"] = "M8,1 A7,7 0 1 1 7.99,1 Z M7.1,7 L7.1,12.5 L8.9,12.5 L8.9,7 Z M7,3.6 L7,5.4 L9,5.4 L9,3.6 Z",
        ["lightbulb"] = "M8,1 C5,1 3,3.2 3,6 C3,8 4.2,9.2 5.2,10.2 L5.6,12 L10.4,12 L10.8,10.2 C11.8,9.2 13,8 13,6 C13,3.2 11,1 8,1 Z M6,13 L10,13 L10,14 L6,14 Z M6.6,14.6 L9.4,14.6 L9.4,15.4 L6.6,15.4 Z",
        ["lightbulb.fill"] = "M8,1 C5,1 3,3.2 3,6 C3,8 4.2,9.2 5.2,10.2 L5.6,12 L10.4,12 L10.8,10.2 C11.8,9.2 13,8 13,6 C13,3.2 11,1 8,1 Z M6,13 L10,13 L10,14 L6,14 Z M6.6,14.6 L9.4,14.6 L9.4,15.4 L6.6,15.4 Z",
        ["checkmark.seal"] = "M8,0.8 L10,2.4 L12.6,2.2 L13.2,4.8 L15.2,6.4 L14.2,8.8 L14.8,11.4 L12.4,12.4 L11.2,14.8 L8.6,14.2 L6.4,15.6 L4.8,13.4 L2.2,13 L2.2,10.4 L0.6,8.4 L2,6.2 L1.8,3.6 L4.4,3 L6,0.8 Z M4.6,8.2 L3.6,9.2 L6.8,12 L12.4,5.6 L11.4,4.6 L6.8,10 Z",
        ["wand.and.stars"] = "M1.5,13.4 L10.4,4.5 L11.5,5.6 L2.6,14.5 Z M11,0.8 L11.6,2.4 L13.2,3 L11.6,3.6 L11,5.2 L10.4,3.6 L8.8,3 L10.4,2.4 Z M14,6 L14.4,7.1 L15.5,7.5 L14.4,7.9 L14,9 L13.6,7.9 L12.5,7.5 L13.6,7.1 Z M5,1.5 L5.4,2.6 L6.5,3 L5.4,3.4 L5,4.5 L4.6,3.4 L3.5,3 L4.6,2.6 Z",
        ["hammer"] = "M2.5,2.5 L8.5,2.5 L11.5,4.5 L14.5,4.5 L14.5,8 L11.5,8 L10,6.8 L9,7.8 L13.8,13.6 L12.4,15 L6.6,9.8 L5.2,8.6 L2.5,8.6 Z",
        ["arrow.right.circle"] = "M8,1 A7,7 0 1 1 7.99,1 Z M8,2.4 A5.6,5.6 0 1 0 8.01,2.4 Z M4.5,7.3 L9.4,7.3 L7.6,5.5 L8.6,4.5 L12.1,8 L8.6,11.5 L7.6,10.5 L9.4,8.7 L4.5,8.7 Z",
        ["magnifyingglass"] = "M6.5,1 A5.5,5.5 0 1 1 6.49,1 Z M6.5,2.6 A3.9,3.9 0 1 0 6.51,2.6 Z M10.2,11.3 L11.3,10.2 L15.2,14.1 L14.1,15.2 Z",
        ["pencil"] = "M11.4,1.6 L14.4,4.6 L5.4,13.6 L1.6,14.4 L2.4,10.6 Z M10.4,4.2 L3.6,11 L5,12.4 L11.8,5.6 Z",
        ["text.alignleft"] = "M1.5,2.5 L14.5,2.5 L14.5,4 L1.5,4 Z M1.5,6 L10.5,6 L10.5,7.5 L1.5,7.5 Z M1.5,9.5 L14.5,9.5 L14.5,11 L1.5,11 Z M1.5,13 L9,13 L9,14.5 L1.5,14.5 Z",
        ["text.badge.minus"] = "M1.5,3 L10,3 L10,4.5 L1.5,4.5 Z M1.5,7 L8,7 L8,8.5 L1.5,8.5 Z M1.5,11 L7,11 L7,12.5 L1.5,12.5 Z M12.2,8 A3.3,3.3 0 1 1 12.19,8 Z M10.2,10.7 L10.2,11.9 L14.2,11.9 L14.2,10.7 Z",
        ["circle.fill"] = "M8,2 A6,6 0 1 1 7.99,2 Z",
        ["chevron.compact.right"] = "M6,3 L7.3,2.2 L11,8 L7.3,13.8 L6,13 L9.2,8 Z",
        ["bolt"] = "M9.2,1 L3.5,9 L7.4,9 L6.6,15 L12.5,6.8 L8.6,6.8 Z",
        ["square.fill"] = "M3,1.5 L13,1.5 Q14.5,1.5 14.5,3 L14.5,13 Q14.5,14.5 13,14.5 L3,14.5 Q1.5,14.5 1.5,13 L1.5,3 Q1.5,1.5 3,1.5 Z",
    };

    /// <summary>Gets the geometry of a symbol (SF Symbol name), or a filled square for unknown names.</summary>
    public static Geometry Geometry(string symbol) =>
        Avalonia.Media.Geometry.Parse(Paths.TryGetValue(symbol, out var data) ? data : Paths["square.fill"]);

    /// <summary>A symbol as a control of <paramref name="size"/> points, filled with <paramref name="color"/>.</summary>
    public static Control Icon(string symbol, Color color, double size = 13) => new Path
    {
        Data = Geometry(symbol),
        Fill = new SolidColorBrush(color),
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A filled rounded square with a short label: the "c.square.fill" family of SF Symbols used for completion kinds.
    /// </summary>
    public static Control LetterSquare(string letter, Color color, double size = 14)
    {
        var square = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        square.Child = letter switch
        {
            "bolt" => new Path { Data = Geometry("bolt"), Fill = Brushes.White, Stretch = Stretch.Uniform, Margin = new Thickness(3) },
            "" => null,
            _ => new TextBlock
            {
                Text = letter,
                FontSize = letter.Length > 1 ? size * 0.5 : size * 0.68,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return square;
    }
}
