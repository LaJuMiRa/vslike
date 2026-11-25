using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>A colour with a light and a dark variant; <see cref="Current"/> follows the app's theme (NSColor dynamic provider).</summary>
public sealed record DynamicColor(Color Dark, Color Light)
{
    public Color Current => Palette.IsDark ? Dark : Light;

    public IBrush Brush => new SolidColorBrush(Current);

    public static implicit operator DynamicColor(Color color) => new(color, color);
}

/// <summary>
/// Colours of VsLike's own look: slate surfaces with an amber accent (the app icon's colours). Every colour adapts to
/// light and dark appearance.
/// </summary>
public static class Palette
{
    public static readonly DynamicColor Accent = new(Rgb(0.95, 0.70, 0.29), Rgb(0.80, 0.52, 0.08));
    public static readonly DynamicColor EditorBackground = new(Rgb(0.105, 0.115, 0.135), Rgb(0.995, 0.995, 1));
    public static readonly DynamicColor GutterText = new(White(1, 0.28), White(0, 0.3));
    public static readonly DynamicColor GutterActive = new(White(1, 0.75), White(0, 0.7));
    public static readonly DynamicColor CurrentLine = new(White(1, 0.045), Rgb(0.2, 0.3, 0.5, 0.05));
    public static readonly DynamicColor Selection = new(Rgb(0.95, 0.70, 0.29, 0.22), Rgb(0.95, 0.70, 0.29, 0.28));
    public static readonly DynamicColor BracketMatch = new(White(1, 0.14), White(0, 0.1));
    public static readonly DynamicColor ExecutionLine = new(Rgb(0.95, 0.78, 0.2, 0.18), Rgb(1, 0.85, 0.2, 0.35));
    public static readonly DynamicColor Breakpoint = Rgb(0.90, 0.30, 0.27);
    public static readonly DynamicColor Error = Rgb(0.93, 0.33, 0.31);
    public static readonly DynamicColor Warning = Rgb(0.93, 0.68, 0.20);
    public static readonly DynamicColor Info = Rgb(0.36, 0.62, 0.95);
    public static readonly DynamicColor StyleCop = Rgb(0.33, 0.72, 0.68);
    public static readonly DynamicColor Success = Rgb(0.30, 0.74, 0.45);

    // System colours of AppKit used by the editor (labelColor, secondaryLabelColor, separatorColor, popover material).
    public static readonly DynamicColor Label = new(White(1, 0.85), White(0, 0.85));
    public static readonly DynamicColor SecondaryLabel = new(White(1, 0.55), White(0, 0.5));
    public static readonly DynamicColor TertiaryLabel = new(White(1, 0.25), White(0, 0.26));
    public static readonly DynamicColor Separator = new(White(1, 0.1), White(0, 0.1));
    public static readonly DynamicColor Popover = new(Rgb(0.165, 0.173, 0.192, 0.98), Rgb(0.965, 0.965, 0.972, 0.98));
    public static readonly DynamicColor QuaternaryFill = new(White(1, 0.1), White(0, 0.05));
    public static readonly DynamicColor RowSelection = new(Rgb(0.95, 0.70, 0.29, 0.35), Rgb(0.80, 0.52, 0.08, 0.25));

    /// <summary>Gets a value indicating whether the app shows the dark appearance.</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static DynamicColor ColorFor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Error,
        DiagnosticSeverity.Warning => Warning,
        _ => Info,
    };

    public static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

    internal static Color Rgb(double red, double green, double blue, double alpha = 1) =>
        Color.FromArgb(ToByte(alpha), ToByte(red), ToByte(green), ToByte(blue));

    internal static Color White(double white, double alpha) => Rgb(white, white, white, alpha);

    internal static Color Hex(uint hex, double alpha = 1) =>
        Color.FromArgb(ToByte(alpha), (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>Syntax colours for lexical tokens and Roslyn's semantic classifications.</summary>
public static class SyntaxColors
{
    public static readonly DynamicColor Text = Dynamic(0xDCDFE6, 0x1F2329);
    public static readonly DynamicColor Keyword = Dynamic(0xFF8FB1, 0xC2185B);
    public static readonly DynamicColor Type = Dynamic(0x6CD4C4, 0x00796B);
    public static readonly DynamicColor Interface = Dynamic(0x9BE7C4, 0x2E7D5B);
    public static readonly DynamicColor Method = Dynamic(0x82B8FF, 0x1E5FB8);
    public static readonly DynamicColor Member = Dynamic(0xC0AEFF, 0x6A4BC4);
    public static readonly DynamicColor Local = Dynamic(0xE8EAF0, 0x2A2F37);
    public static readonly DynamicColor Parameter = Dynamic(0xF2D7A6, 0x7A4F12);
    public static readonly DynamicColor String = Dynamic(0xF2C97D, 0xA35A00);
    public static readonly DynamicColor Number = Dynamic(0x9BE2A0, 0x2E7D32);
    public static readonly DynamicColor Comment = Dynamic(0x6F7787, 0x8A909A);
    public static readonly DynamicColor Preprocessor = Dynamic(0xE8A06C, 0xB35A1F);
    public static readonly DynamicColor Namespace = Dynamic(0xC8CDD6, 0x4A515C);
    public static readonly DynamicColor Punctuation = Dynamic(0xA9AFBB, 0x555B66);

    public static DynamicColor ColorFor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword or TokenKind.ControlKeyword or TokenKind.TypeKeyword => Keyword,
        TokenKind.Identifier => Text,
        TokenKind.Number => Number,
        TokenKind.String or TokenKind.Character => String,
        TokenKind.Comment or TokenKind.DocComment => Comment,
        TokenKind.Preprocessor => Preprocessor,
        _ => Punctuation,
    };

    /// <summary>Colour for a Roslyn classification name (see SemanticClassifications in the engine), null keeps the lexical one.</summary>
    public static DynamicColor? ColorFor(string classification) => classification switch
    {
        "class" or "struct" or "enum" or "delegate" or "record" or "typeParameter" => Type,
        "interface" => Interface,
        "method" or "extensionMethod" => Method,
        "property" or "field" or "event" or "constant" or "enumMember" => Member,
        "parameter" => Parameter,
        "local" => Local,
        "namespace" => Namespace,
        "keyword" => Keyword,
        _ => null,
    };

    private static DynamicColor Dynamic(uint dark, uint light) => new(Palette.Hex(dark), Palette.Hex(light));
}
