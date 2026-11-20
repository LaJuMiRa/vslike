using Avalonia.Controls;
using Avalonia.Media;

namespace VsLike.Diagram.View;

/// <summary>Farben der Zeichenfläche aus den Theme-Tokens der IDE (mit neutralen Rückfallwerten).</summary>
internal sealed record Palette(IBrush Background, IBrush Surface, IBrush ExternalFill, IBrush Text, IBrush Muted, IBrush Line, IBrush Accent, IBrush Grid)
{
    public static readonly FontFamily MonoFont = new("JetBrains Mono, Menlo, DejaVu Sans Mono, monospace");

    public static Palette Resolve(Control control)
    {
        IBrush Get(string key, string fallback) => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brush.Parse(fallback);
        var text = Get("VsText", "#1f2329");
        var muted = Get("VsTextMuted", "#6b7280");
        var grid = muted is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.12) : (IBrush)Brush.Parse("#20808080");
        var external = muted is ISolidColorBrush m ? new SolidColorBrush(m.Color, 0.15) : (IBrush)Brush.Parse("#20808080");
        return new Palette(Get("VsBackground", "#fbfaf7"), Get("VsSurfaceRaised", "#ffffff"), external, text, muted, text, Get("VsAccent", "#e2a63b"), grid);
    }
}
