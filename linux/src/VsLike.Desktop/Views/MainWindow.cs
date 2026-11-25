using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VsLike.Desktop;

/// <summary>
/// The main window (the <c>Window("VsLike", id: "main")</c> scene of VsLikeApp.swift). Linux: the window draws its own
/// title bar like the unified macOS toolbar — the toolbar is the title bar, the window buttons sit on the right (Linux
/// convention) and the menus are in a menu button at the top left (there is no global menu bar). Layers above the content:
/// command palette, sheets and alerts.
/// </summary>
public sealed class MainWindow : Window, IWorkspaceDialogs
{
    private readonly Workspace _workspace;
    private readonly Panel _alertLayer = new();

    public MainWindow(Workspace workspace)
    {
        _workspace = workspace;
        Title = "VsLike";
        Width = 1440;
        Height = 900;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Own title bar; the window manager keeps the resizable border.
        WindowDecorations = WindowDecorations.BorderOnly;
        Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://vslike/Assets/vslike-256.png"))));
        MainView = new MainView(workspace, this);
        Content = new Panel
        {
            Children =
            {
                MainView,
                new Live(PaletteLayer, workspace) { Only = new HashSet<string> { nameof(Workspace.IsCommandPalettePresented), nameof(Workspace.IsQuickOpenPresented) } },
                new SheetHost(workspace, this),
                _alertLayer,
            },
        };
        workspace.Dialogs = this;
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.Solution) or nameof(Workspace.RootPath))
            {
                UpdateTitle();
            }
        };
        KeyBindings.AddRange(AppCommands.KeyBindings(workspace, this));
        UpdateTitle();
    }

    public MainView MainView { get; }

    public Workspace Workspace => _workspace;

    public Task<SaveChoice> ConfirmSave(IReadOnlyList<string> names)
    {
        var message = names.Count == 1
            ? L.T("Save changes to %@?", names[0])
            : L.T("Save changes to %lld files?", names.Count);
        return Alerts.Ask(
            _alertLayer,
            message,
            L.T("Your changes will be lost if you don't save them."),
            [(L.T("Save"), SaveChoice.Save, true), (L.T("Cancel"), SaveChoice.Cancel, false), (L.T("Don't Save"), SaveChoice.Discard, false)],
            SaveChoice.Cancel);
    }

    public Task<bool> Confirm(string message, string confirmTitle) =>
        Alerts.Ask(_alertLayer, message, null, [(confirmTitle, true, true), (L.T("Cancel"), false, false)], false);

    /// <summary>Shows a plain alert (workspace.alert).</summary>
    public Task ShowAlert(string title, string message) =>
        Alerts.Ask(_alertLayer, title, message, [(L.T("OK"), true, true)], true);

    private void UpdateTitle()
    {
        var subtitle = _workspace.Solution is { } solution ? $" — {solution.Projects.Count} " + L.T("projects") : string.Empty;
        Title = _workspace.Title + subtitle;
    }

    private Control PaletteLayer() =>
        _workspace.IsCommandPalettePresented || _workspace.IsQuickOpenPresented
            ? new CommandPalette(_workspace, this, _workspace.IsQuickOpenPresented ? CommandPalette.Mode.Files : CommandPalette.Mode.Commands)
            : new Panel { IsHitTestVisible = false };
}

/// <summary>The title bar parts the window draws itself (no Swift original: macOS draws them).</summary>
public static class TitleBar
{
    public const double Height = 52;

    /// <summary>Moves the window when the empty title bar area is dragged; a double click maximizes or restores it.</summary>
    public static T Draggable<T>(T control, Window window)
        where T : Control
    {
        if (control is Panel panel && panel.Background is null)
        {
            panel.Background = Brushes.Transparent;
        }

        control.PointerPressed += (_, e) =>
        {
            if (e.Source == control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed && e.ClickCount == 1)
            {
                window.BeginMoveDrag(e);
            }
        };
        control.DoubleTapped += (_, e) =>
        {
            if (e.Source == control)
            {
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
        };
        return control;
    }

    /// <summary>The top of the sidebar column: menu button on the left, sidebar button on the right.</summary>
    public static Control SidebarTop(MainWindow window, Action toggleSidebar)
    {
        var menu = MenuButton(window);
        var toggle = Ui.IconButton("sidebar.left", toggleSidebar, L.T("Hide Sidebar"), 17, "TextSecondary", toolbar: true);
        var row = Ui.Row([menu], [toggle], 0);
        row.Height = Height;
        row.Margin = new Thickness(12, 0, 10, 0);
        return Draggable(row, window);
    }

    /// <summary>The menu button with all menus of the app (File, Edit, Code, Navigate, Build, Debug, View).</summary>
    public static Button MenuButton(MainWindow window)
    {
        var button = Ui.IconButton("line.3.horizontal", () => { }, L.T("Menu"), 17, "TextSecondary", toolbar: true);
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opening += (_, _) => flyout.ItemsSource = Ui.Menu(AppCommands.Menus(window.Workspace, window));
        button.Flyout = flyout;
        return button;
    }

    /// <summary>Minimize, maximize and close on the right end of the title bar (Linux convention).</summary>
    public static Control WindowButtons(Window window)
    {
        var minimize = WindowButton("minus", () => window.WindowState = WindowState.Minimized, L.T("Minimize"));
        var maximize = WindowButton("square", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, L.T("Zoom"));
        var close = WindowButton("xmark", window.Close, L.T("Close"));
        var stack = Ui.HStack(6, minimize, maximize, close);
        stack.Margin = new Thickness(10, 0, 12, 0);
        return stack;
    }

    private static Button WindowButton(string symbol, Action action, string tip)
    {
        var button = new Button
        {
            Content = Icons.Make(symbol, 10, "TextPrimary", 2.4),
            Classes = { "borderless" },
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Ui.Res(button, Button.BackgroundProperty, "FillQuaternary");
        button.Click += (_, _) => action();
        return Ui.Tip(button, tip);
    }
}
