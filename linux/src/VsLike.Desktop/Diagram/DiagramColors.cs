using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// The colours the class diagram uses, in light and dark appearance. Values from <c>Palette</c> and <c>SyntaxColors</c>
/// in EditorTheme.swift and from the macOS system colours the SwiftUI views use (primary/secondary label,
/// controlBackgroundColor). Kept in the diagram folder so the diagram does not depend on the editor port.
/// </summary>
internal static class DiagramColors
{
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static Color Accent => IsDark ? Rgb(0.95, 0.70, 0.29) : Rgb(0.80, 0.52, 0.08);

    public static Color EditorBackground => IsDark ? Rgb(0.105, 0.115, 0.135) : Rgb(0.995, 0.995, 1);

    /// <summary>Gets macOS <c>controlBackgroundColor</c>: the box body.</summary>
    public static Color ControlBackground => IsDark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White;

    /// <summary>Gets SwiftUI <c>Color.primary</c> (label colour).</summary>
    public static Color Primary => IsDark ? Colors.White : Colors.Black;

    /// <summary>Gets macOS <c>secondaryLabelColor</c>.</summary>
    public static Color Secondary => IsDark ? Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0x00, 0x00, 0x00);

    /// <summary>Gets macOS <c>tertiaryLabelColor</c>.</summary>
    public static Color Tertiary => IsDark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x42, 0x00, 0x00, 0x00);

    /// <summary>Gets the SwiftUI <c>Divider</c> colour (macOS separatorColor).</summary>
    public static Color Separator => IsDark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00);

    public static Color Type => Syntax(0x6CD4C4, 0x00796B);

    public static Color Interface => Syntax(0x9BE7C4, 0x2E7D5B);

    public static Color Method => Syntax(0x82B8FF, 0x1E5FB8);

    public static Color Member => Syntax(0xC0AEFF, 0x6A4BC4);

    public static Color String => Syntax(0xF2C97D, 0xA35A00);

    public static Color Number => Syntax(0x9BE2A0, 0x2E7D32);

    /// <summary>Header colour of a type box (TypeBoxView.color(for:)).</summary>
    public static Color ForKind(DiagramTypeKind kind) => kind switch
    {
        DiagramTypeKind.Class => Type,
        DiagramTypeKind.Interface => Interface,
        DiagramTypeKind.Struct or DiagramTypeKind.RecordStruct => Method,
        DiagramTypeKind.Record => Member,
        _ => String,
    };

    public static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    public static IBrush Brush(Color color) => new ImmutableSolidColorBrush(color);

    public static IBrush Brush(Color color, double opacity) => new ImmutableSolidColorBrush(WithOpacity(color, opacity));

    private static Color Syntax(uint dark, uint light)
    {
        var hex = IsDark ? dark : light;
        return Color.FromRgb((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
    }

    private static Color Rgb(double r, double g, double b) =>
        Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
}
