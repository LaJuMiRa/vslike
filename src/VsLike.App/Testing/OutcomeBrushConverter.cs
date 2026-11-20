using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VsLike.App.Testing;

/// <summary>Farbe des Ergebnis-Symbols aus den Theme-Tokens.</summary>
public sealed class OutcomeBrushConverter : IValueConverter
{
    public static readonly OutcomeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "passed" => "VsSuccess",
            "failed" => "VsError",
            "running" => "VsAccent",
            _ => "VsTextMuted",
        };
        return Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out var brush) == true ? brush as IBrush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
