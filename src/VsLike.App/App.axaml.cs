using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Localization;
using VsLike.Core.Platform;

namespace VsLike.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices(new PlatformService());
            Loc.SetCulture(services.Settings.Language);
            ThemeService.Apply(services.Settings.Theme);
            var shell = new ShellViewModel(services, ShellPanels.CreateDefault());
            var window = new MainWindow(services, shell);
            shell.QuitRequested += (_, _) => desktop.Shutdown();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
