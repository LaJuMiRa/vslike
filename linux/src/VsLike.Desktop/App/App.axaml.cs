using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VsLike.Desktop.Diagram;

namespace VsLike.Desktop;

/// <summary>The application (VsLikeApp.swift): creates the workspace and the main window and opens the files given on the command line.</summary>
public sealed class App : Application
{
    private static readonly string[] RootExtensions = [".sln", ".slnx", ".csproj"];

    private bool _closing;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        L.Culture = System.Globalization.CultureInfo.GetCultureInfo(Preferences.Shared.EffectiveLanguage);
        Preferences.Shared.ApplyAppearance();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Workspace.DiagramFactory = new DiagramEditorFactory();
            var workspace = new Workspace();
            var window = new MainWindow(workspace);
            window.Closing += (_, e) => OnClosing(workspace, window, e);
            desktop.MainWindow = window;
            window.Opened += (_, _) => _ = Open(workspace, [.. (desktop.Args ?? []).Where(a => !a.StartsWith('-')).Select(Path.GetFullPath)]);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Opens a solution, project or folder first (if any), then the remaining files as tabs. A file opened on its own
    /// brings its solution or project along, so that the code behind it is analysed.
    /// </summary>
    internal static async Task Open(Workspace workspace, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var enclosing = workspace.RootPath is null ? EnclosingSolution(paths[0]) : null;
        var root = paths.FirstOrDefault(IsRoot) ?? enclosing ?? paths[0];
        if (workspace.RootPath is null || IsRoot(root))
        {
            await workspace.Open(root);
        }

        foreach (var file in paths.Where(p => p != root || !IsRoot(root)))
        {
            workspace.OpenFile(file);
        }
    }

    /// <summary>The nearest solution above a file (preferred), otherwise the nearest project, searching up to six folders.</summary>
    internal static string? EnclosingSolution(string file)
    {
        if (Directory.Exists(file))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(file);
        string? project = null;
        for (var i = 0; i < 6 && folder is not null && Directory.Exists(folder); i++)
        {
            var entries = Directory.GetFileSystemEntries(folder);
            var solution = entries
                .Where(e => Path.GetExtension(e).ToLowerInvariant() is ".slnx" or ".sln")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (solution is not null)
            {
                return solution;
            }

            project ??= entries.FirstOrDefault(e => Path.GetExtension(e).Equals(".csproj", StringComparison.OrdinalIgnoreCase));
            folder = Path.GetDirectoryName(folder);
        }

        return project;
    }

    private static bool IsRoot(string path) =>
        RootExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) || Directory.Exists(path);

    /// <summary>Asks about unsaved changes before the window closes (applicationShouldTerminate).</summary>
    private void OnClosing(Workspace workspace, MainWindow window, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        _ = CloseAfterConfirm(workspace, window);
    }

    private async Task CloseAfterConfirm(Workspace workspace, MainWindow window)
    {
        if (!await workspace.CloseAll())
        {
            return;
        }

        workspace.Terminals.StopRuns();
        _closing = true;
        window.Close();
    }
}
