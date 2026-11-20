using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VsLike.App.Dialogs;
using VsLike.App.Services;
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
            Editor.EditorView.EditorFontSize = services.Settings.EditorFontSize;
            MainWindow? window = null;
            var workbench = new Workbench(services, new DialogService(() => window), () => window);
            window = new MainWindow(workbench);
            workbench.Shell.QuitRequested += (_, _) => window.Close();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => workbench.Dispose();
            window.Opened += async (_, _) => await OpenStartupPathsAsync(workbench, desktop.Args ?? []).ConfigureAwait(true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Opens folders and files passed on the command line (also used by file associations).</summary>
    private static async Task OpenStartupPathsAsync(Workbench workbench, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                workbench.Explorer.OpenFolder(path);
            }
            else if (File.Exists(path))
            {
                await workbench.Documents.OpenAsync(path).ConfigureAwait(true);
            }
        }
    }
}
