using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using VsLike.App.Services;

namespace VsLike.App.Shell;

/// <summary>
/// Hängt alle Tastenkürzel als <see cref="KeyBinding"/> ans Fenster. Auf macOS übernimmt das native Menü
/// die Kürzel der Menübefehle; dort werden nur Befehle ohne Menüeintrag gebunden, sonst würden sie doppelt feuern.
/// </summary>
public static class KeyBindingInstaller
{
    public static void Install(Window window, AppServices services)
    {
        window.KeyBindings.Clear();
        foreach (var command in services.Commands.Commands)
        {
            if (!command.BindGlobally || (services.Platform.HasNativeMenuBar && services.Menus.Contains(command.Id)))
            {
                continue;
            }

            var chord = services.Commands.GetChord(command.Id);
            var gesture = chord is null ? null : KeyChordConverter.ToGesture(chord, services.Platform.Kind);
            if (gesture is null)
            {
                continue;
            }

            var id = command.Id;
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = gesture,
                Command = new AsyncRelayCommand(() => services.Commands.ExecuteAsync(id)),
            });
        }
    }
}
