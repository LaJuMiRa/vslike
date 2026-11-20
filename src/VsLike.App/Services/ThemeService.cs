using Avalonia;
using Avalonia.Styling;
using VsLike.Core.Settings;

namespace VsLike.App.Services;

public static class ThemeService
{
    public static void Apply(ThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = mode switch
        {
            ThemeMode.Dark => ThemeVariant.Dark,
            ThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
