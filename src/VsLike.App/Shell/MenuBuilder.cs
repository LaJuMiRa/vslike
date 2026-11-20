using Avalonia.Controls;
using VsLike.App.Services;
using VsLike.Core.Commands;
using VsLike.Core.Localization;

namespace VsLike.App.Shell;

/// <summary>
/// Erzeugt die Menüs aus <see cref="MenuRegistry"/>: native Menüleiste auf macOS, Menü im Fenster auf Linux.
/// </summary>
public static class MenuBuilder
{
    /// <summary>
    /// Replaces the items of an existing native menu. The menu object itself must stay the same:
    /// Avalonia.Native crashes ("menu being updated does not match") when a window gets a new NativeMenu.
    /// </summary>
    public static void FillNative(NativeMenu menu, AppServices services)
    {
        menu.Items.Clear();
        foreach (var top in MenuRegistry.TopLevelMenus)
        {
            var entries = services.Menus.GetEntries(top);
            if (entries.Count == 0)
            {
                continue;
            }

            var sub = new NativeMenu();
            foreach (var item in WithSeparators(entries))
            {
                sub.Add(item is null ? new NativeMenuItemSeparator() : CreateNativeItem(services, item));
            }

            menu.Add(new NativeMenuItem(Loc.Get(top)) { Menu = sub });
        }
    }

    public static IReadOnlyList<Control> BuildInWindow(AppServices services)
    {
        var result = new List<Control>();
        foreach (var top in MenuRegistry.TopLevelMenus)
        {
            var entries = services.Menus.GetEntries(top);
            if (entries.Count == 0)
            {
                continue;
            }

            var items = WithSeparators(entries)
                .Select(e => e is null ? (Control)new Separator() : CreateMenuItem(services, e))
                .ToList();
            result.Add(new MenuItem { Header = Loc.Get(top), ItemsSource = items });
        }

        return result;
    }

    private static IEnumerable<MenuEntry?> WithSeparators(IReadOnlyList<MenuEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0 && entries[i].Group != entries[i - 1].Group)
            {
                yield return null;
            }

            yield return entries[i];
        }
    }

    private static NativeMenuItem CreateNativeItem(AppServices services, MenuEntry entry)
    {
        var command = services.Commands.Find(entry.CommandId);
        var chord = services.Commands.GetChord(entry.CommandId);
        var item = new NativeMenuItem(command is null ? entry.CommandId : Loc.Get(command.TitleKey))
        {
            Gesture = chord is null ? null : KeyChordConverter.ToGesture(chord, services.Platform.Kind),
        };
        item.Click += async (_, _) => await services.Commands.ExecuteAsync(entry.CommandId).ConfigureAwait(true);
        return item;
    }

    private static MenuItem CreateMenuItem(AppServices services, MenuEntry entry)
    {
        var command = services.Commands.Find(entry.CommandId);
        var chord = services.Commands.GetChord(entry.CommandId);
        var item = new MenuItem
        {
            Header = command is null ? entry.CommandId : Loc.Get(command.TitleKey),
            InputGesture = chord is null ? null : KeyChordConverter.ToGesture(chord, services.Platform.Kind),
        };
        item.Click += async (_, _) => await services.Commands.ExecuteAsync(entry.CommandId).ConfigureAwait(true);
        return item;
    }
}
