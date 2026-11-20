using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using VsLike.App.Services;
using VsLike.Core.Commands;
using VsLike.Core.Layout;
using VsLike.Core.Localization;
using VsLike.Core.Settings;

namespace VsLike.App.Shell;

/// <summary>
/// Hauptfenster-Zustand: Dock-Layout, Befehlspalette, Statusleiste und die Shell-Befehle.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public const string PaletteCommandId = "shell.palette";

    private readonly AppServices _services;
    private LayoutState _layoutState;

    [ObservableProperty]
    private IRootDock? _layout;

    [ObservableProperty]
    private string _statusText = Loc.Get("Status.Ready");

    [ObservableProperty]
    private string _caretText = string.Empty;

    [ObservableProperty]
    private string _problemsText = string.Empty;

    [ObservableProperty]
    private string _analyzerText = string.Empty;

    public ShellViewModel(AppServices services, IEnumerable<(PanelDescriptor Descriptor, object Content)> panels)
    {
        _services = services;
        Factory = new DockFactory(panels.Select(p => new PanelViewModel(p.Descriptor, p.Content)));
        Palette = new CommandPaletteViewModel(services.Commands, services.Platform.Kind);
        _layoutState = services.LayoutStore.Load();
        RegisterCommands();
        var paletteChord = services.Commands.GetChord(PaletteCommandId)?.ToDisplayString(services.Platform.Kind) ?? string.Empty;
        Documents = [new WelcomeViewModel(paletteChord)];
        Rebuild(activeDocument: null);
        Loc.CultureChanged += (_, _) => StatusText = Loc.Get("Status.Ready");
    }

    /// <summary>Raised when the user asks to quit (Linux menu; macOS uses the native app menu).</summary>
    public event EventHandler? QuitRequested;

    public DockFactory Factory { get; }

    public CommandPaletteViewModel Palette { get; }

    public List<IDockable> Documents { get; }

    public LayoutState LayoutState => _layoutState;

    public bool IsPanelVisible(string panelId)
        => Layout is not null && Factory.Capture(Layout, _layoutState).Panels.Any(p => p.PanelId == panelId && p.IsVisible);

    public void TogglePanel(string panelId)
    {
        var current = CaptureLayout();
        var placements = current.Panels
            .Select(p => p.PanelId == panelId ? p with { IsVisible = !p.IsVisible, IsActive = !p.IsVisible } : p)
            .ToList();
        _layoutState = current with { Panels = placements };
        Rebuild(Factory.DocumentDock?.ActiveDockable);
    }

    /// <summary>Makes a panel visible and selects its tab.</summary>
    public void ShowPanel(string panelId)
    {
        if (!IsPanelVisible(panelId))
        {
            TogglePanel(panelId);
        }

        if (Factory.Panels.FirstOrDefault(p => p.Id == panelId) is { } panel)
        {
            Factory.SetActiveDockable(panel);
        }
    }

    public void ResetLayout()
    {
        _layoutState = new LayoutState { WindowWidth = _layoutState.WindowWidth, WindowHeight = _layoutState.WindowHeight };
        Rebuild(Factory.DocumentDock?.ActiveDockable);
    }

    /// <summary>Captures the live layout and window size and writes layout.json.</summary>
    public void SaveLayout(double width, double height, bool maximized)
    {
        _layoutState = CaptureLayout() with { WindowWidth = width, WindowHeight = height, IsMaximized = maximized };
        _services.LayoutStore.Save(_layoutState);
    }

    public LayoutState CaptureLayout() => Layout is null ? _layoutState : Factory.Capture(Layout, _layoutState);

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private void Rebuild(IDockable? activeDocument)
    {
        var documents = Factory.DocumentDock?.VisibleDockables?.ToList() ?? Documents;
        var root = Factory.Build(_layoutState, documents, activeDocument);
        Factory.InitLayout(root);
        Layout = root;
    }

    private void RegisterCommands()
    {
        var commands = _services.Commands;
        var menus = _services.Menus;

        Register(PaletteCommandId, "Command.Palette", "Category.View", "Cmd+Shift+P", () => Palette.Open());
        menus.Add("Menu.View", PaletteCommandId);

        foreach (var panel in Factory.Panels.OrderBy(p => p.Descriptor.DefaultArea).ThenBy(p => p.Id))
        {
            var id = $"view.panel.{panel.Id}";
            commands.Register(new CommandDefinition
            {
                Id = id,
                TitleKey = panel.Descriptor.TitleKey,
                CategoryKey = "Category.View",
                DefaultChord = panel.Descriptor.ToggleChord is null ? null : KeyChord.Parse(panel.Descriptor.ToggleChord),
                Execute = () => Run(() => TogglePanel(panel.Id)),
            });
            menus.Add("Menu.View", id, group: 1);
        }

        Register("view.theme.dark", "Command.Theme.Dark", "Category.View", null, () => SetTheme(ThemeMode.Dark));
        Register("view.theme.light", "Command.Theme.Light", "Category.View", null, () => SetTheme(ThemeMode.Light));
        Register("view.theme.system", "Command.Theme.System", "Category.View", null, () => SetTheme(ThemeMode.System));
        Register("view.language.de", "Command.Language.German", "Category.View", null, () => SetLanguage("de"));
        Register("view.language.en", "Command.Language.English", "Category.View", null, () => SetLanguage("en"));
        foreach (var id in new[] { "view.theme.dark", "view.theme.light", "view.theme.system" })
        {
            menus.Add("Menu.View", id, group: 2);
        }

        menus.Add("Menu.View", "view.language.de", group: 3);
        menus.Add("Menu.View", "view.language.en", group: 3);

        Register("window.layout.reset", "Command.Layout.Reset", "Category.View", null, ResetLayout);
        menus.Add("Menu.Window", "window.layout.reset");

        if (!_services.Platform.HasNativeMenuBar)
        {
            Register("app.quit", "Command.Quit", "Category.File", "Cmd+Q", () => QuitRequested?.Invoke(this, EventArgs.Empty));
            menus.Add("Menu.File", "app.quit", group: 99);
        }
    }

    private void Register(string id, string titleKey, string categoryKey, string? chord, Action action)
        => _services.Commands.Register(new CommandDefinition
        {
            Id = id,
            TitleKey = titleKey,
            CategoryKey = categoryKey,
            DefaultChord = chord is null ? null : KeyChord.Parse(chord),
            Execute = () => Run(action),
        });

    private void SetTheme(ThemeMode mode)
    {
        _services.UpdateSettings(s => s with { Theme = mode });
        ThemeService.Apply(mode);
    }

    private void SetLanguage(string language)
    {
        _services.UpdateSettings(s => s with { Language = language });
        Loc.SetCulture(language);
    }
}
