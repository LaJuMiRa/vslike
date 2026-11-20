using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

            // macOS: Dateien, die per Finder/Dateizuordnung geöffnet werden, kommen als Aktivierung statt als Argument.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs files)
                    {
                        var paths = files.Files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                        await OpenStartupPathsAsync(workbench, paths).ConfigureAwait(true);
                    }
                };
            }

            window.Opened += async (_, _) =>
            {
                if (!await SdkCheck.EnsureSdkAsync(workbench).ConfigureAwait(true))
                {
                    return;
                }

                await OpenStartupPathsAsync(workbench, desktop.Args ?? []).ConfigureAwait(true);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsSolutionOrProject(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj";

    /// <summary>Opens folders and files passed on the command line (also used by file associations).</summary>
    private static async Task OpenStartupPathsAsync(Workbench workbench, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (IsSolutionOrProject(path) && File.Exists(path))
            {
                await workbench.OpenSolutionAsync(path).ConfigureAwait(true);
            }
            else if (Directory.Exists(path))
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
