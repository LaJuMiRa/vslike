using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace VsLike.Desktop;

/// <summary>Placeholder application until the app shell is ported: one empty window titled "VsLike".</summary>
public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        L.Culture = System.Globalization.CultureInfo.GetCultureInfo(Preferences.Shared.EffectiveLanguage);
        Preferences.Shared.ApplyAppearance();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window { Title = "VsLike", Width = 1440, Height = 900, MinWidth = 900, MinHeight = 560 };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
