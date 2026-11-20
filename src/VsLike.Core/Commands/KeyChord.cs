using System.Text;
using VsLike.Core.Platform;

namespace VsLike.Core.Commands;

/// <summary>
/// Plattformneutrales Tastenkürzel. <see cref="ChordModifiers.Primary"/> ist ⌘ auf macOS und Ctrl auf Linux.
/// </summary>
public sealed record KeyChord(ChordModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, ChordModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Primary"] = ChordModifiers.Primary,
        ["Cmd"] = ChordModifiers.Primary,
        ["Mod"] = ChordModifiers.Primary,
        ["Shift"] = ChordModifiers.Shift,
        ["Alt"] = ChordModifiers.Alt,
        ["Option"] = ChordModifiers.Alt,
        ["Ctrl"] = ChordModifiers.Control,
        ["Control"] = ChordModifiers.Control,
    };

    /// <summary>Parses strings such as "Cmd+Shift+P" or "F5".</summary>
    public static KeyChord Parse(string text)
    {
        if (!TryParse(text, out var chord))
        {
            throw new FormatException($"Invalid key chord: '{text}'.");
        }

        return chord;
    }

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = new KeyChord(ChordModifiers.None, string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ChordModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierNames.TryGetValue(parts[i], out var modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        var key = parts[^1];
        if (key.Length == 0)
        {
            return false;
        }

        chord = new KeyChord(modifiers, key.Length == 1 ? key.ToUpperInvariant() : key);
        return true;
    }

    /// <summary>Formats the chord for menus and the command palette.</summary>
    public string ToDisplayString(PlatformKind platform)
    {
        var builder = new StringBuilder();
        if (platform == PlatformKind.MacOS)
        {
            if (Modifiers.HasFlag(ChordModifiers.Control))
            {
                builder.Append('⌃');
            }

            if (Modifiers.HasFlag(ChordModifiers.Alt))
            {
                builder.Append('⌥');
            }

            if (Modifiers.HasFlag(ChordModifiers.Shift))
            {
                builder.Append('⇧');
            }

            if (Modifiers.HasFlag(ChordModifiers.Primary))
            {
                builder.Append('⌘');
            }

            return builder.Append(Key).ToString();
        }

        var names = new List<string>();
        if (Modifiers.HasFlag(ChordModifiers.Primary) || Modifiers.HasFlag(ChordModifiers.Control))
        {
            names.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ChordModifiers.Shift))
        {
            names.Add("Shift");
        }

        if (Modifiers.HasFlag(ChordModifiers.Alt))
        {
            names.Add("Alt");
        }

        names.Add(Key);
        return string.Join('+', names);
    }

    public override string ToString()
    {
        var names = new List<string>();
        if (Modifiers.HasFlag(ChordModifiers.Primary))
        {
            names.Add("Cmd");
        }

        if (Modifiers.HasFlag(ChordModifiers.Control))
        {
            names.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ChordModifiers.Shift))
        {
            names.Add("Shift");
        }

        if (Modifiers.HasFlag(ChordModifiers.Alt))
        {
            names.Add("Alt");
        }

        names.Add(Key);
        return string.Join('+', names);
    }
}
