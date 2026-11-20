using VsLike.Core.Commands;
using VsLike.Core.Layout;
using VsLike.Core.Platform;
using VsLike.Core.Settings;

namespace VsLike.App.Services;

/// <summary>
/// Kompositionswurzel: hält die app-weiten Dienste. Einstellungen sind unveränderliche Records und werden ersetzt.
/// </summary>
public sealed class AppServices
{
    private readonly JsonFileStore<AppSettings> _settingsStore;

    public AppServices(IPlatformService platform)
    {
        Platform = platform;
        _settingsStore = new JsonFileStore<AppSettings>(Path.Combine(platform.SettingsDirectory, "settings.json"));
        LayoutStore = new JsonFileStore<LayoutState>(Path.Combine(platform.SettingsDirectory, "layout.json"));
        Settings = _settingsStore.Load();
        Commands.SetOverrides(Settings.KeyBindings);
    }

    public event EventHandler? SettingsChanged;

    public IPlatformService Platform { get; }

    public ICommandRegistry Commands { get; } = new CommandRegistry();

    public MenuRegistry Menus { get; } = new();

    public JsonFileStore<LayoutState> LayoutStore { get; }

    public AppSettings Settings { get; private set; }

    public void UpdateSettings(Func<AppSettings, AppSettings> update)
    {
        Settings = update(Settings);
        _settingsStore.Save(Settings);
        Commands.SetOverrides(Settings.KeyBindings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
