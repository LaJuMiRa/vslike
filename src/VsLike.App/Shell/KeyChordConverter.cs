using Avalonia.Input;
using VsLike.Core.Commands;
using VsLike.Core.Platform;

namespace VsLike.App.Shell;

/// <summary>Übersetzt plattformneutrale <see cref="KeyChord"/>s in Avalonia-<see cref="KeyGesture"/>s.</summary>
public static class KeyChordConverter
{
    private static readonly Dictionary<string, Key> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["."] = Key.OemPeriod,
        [","] = Key.OemComma,
        ["/"] = Key.OemQuestion,
        [";"] = Key.OemSemicolon,
        ["-"] = Key.OemMinus,
        ["="] = Key.OemPlus,
        ["["] = Key.OemOpenBrackets,
        ["]"] = Key.OemCloseBrackets,
        ["`"] = Key.OemTilde,
        ["Enter"] = Key.Enter,
        ["Esc"] = Key.Escape,
        ["Space"] = Key.Space,
    };

    public static KeyGesture? ToGesture(KeyChord chord, PlatformKind platform)
    {
        if (!TryGetKey(chord.Key, out var key))
        {
            return null;
        }

        var modifiers = KeyModifiers.None;
        if (chord.Modifiers.HasFlag(ChordModifiers.Primary))
        {
            modifiers |= platform == PlatformKind.MacOS ? KeyModifiers.Meta : KeyModifiers.Control;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Alt))
        {
            modifiers |= KeyModifiers.Alt;
        }

        return new KeyGesture(key, modifiers);
    }

    private static bool TryGetKey(string name, out Key key)
    {
        if (SpecialKeys.TryGetValue(name, out key))
        {
            return true;
        }

        if (name.Length == 1 && char.IsDigit(name[0]))
        {
            key = Key.D0 + (name[0] - '0');
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out key);
    }
}
