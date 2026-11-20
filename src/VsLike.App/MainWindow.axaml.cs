using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VsLike.App.Services;
using VsLike.App.Shell;
using VsLike.Core.Localization;

namespace VsLike.App;

public partial class MainWindow : Window
{
    private readonly AppServices? _services;
    private readonly ShellViewModel? _shell;
    private readonly Workbench? _workbench;
    private bool _closeConfirmed;

    // Für den XAML-Previewer.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(Workbench workbench)
    {
        _workbench = workbench;
        var services = _services = workbench.Services;
        var shell = _shell = workbench.Shell;
        DataContext = shell;
        InitializeComponent();

        Width = shell.LayoutState.WindowWidth;
        Height = shell.LayoutState.WindowHeight;
        if (shell.LayoutState.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        RebuildMenus();
        services.Commands.Changed += (_, _) => RebuildMenus();
        services.Menus.Changed += (_, _) => RebuildMenus();
        services.SettingsChanged += (_, _) => RebuildMenus();
        Loc.CultureChanged += (_, _) => RebuildMenus();

        shell.Palette.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && shell.Palette.IsOpen)
            {
                Dispatcher.UIThread.Post(() => PaletteInput.Focus(), DispatcherPriority.Input);
            }
        };
        PaletteInput.AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);
        PaletteList.DoubleTapped += async (_, _) => await shell.Palette.ExecuteSelectedAsync().ConfigureAwait(true);
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PaletteOverlay))
            {
                shell.Palette.Close();
            }
        };
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && _workbench is not null && _workbench.Documents.OpenDocuments.Any(d => d.IsDirty))
        {
            e.Cancel = true;
            if (await _workbench.Documents.ConfirmCloseAllAsync().ConfigureAwait(true))
            {
                _closeConfirmed = true;
                Close();
            }

            return;
        }

        _shell?.SaveLayout(Width, Height, WindowState == WindowState.Maximized);
        base.OnClosing(e);
    }

    private async void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _shell.Palette.Close();
                e.Handled = true;
                break;
            case Key.Down:
                _shell.Palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _shell.Palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                await _shell.Palette.ExecuteSelectedAsync().ConfigureAwait(true);
                break;
        }
    }

    private void RebuildMenus()
    {
        if (_services is null)
        {
            return;
        }

        if (_services.Platform.HasNativeMenuBar)
        {
            NativeMenu.SetMenu(this, MenuBuilder.BuildNative(_services));
            if (Application.Current is { } app)
            {
                NativeMenu.SetMenu(app, new NativeMenu());
            }
        }
        else
        {
            InWindowMenu.ItemsSource = MenuBuilder.BuildInWindow(_services);
            InWindowMenu.IsVisible = true;
        }

        KeyBindingInstaller.Install(this, _services);
    }
}
