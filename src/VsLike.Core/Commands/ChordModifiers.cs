namespace VsLike.Core.Commands;

[Flags]
public enum ChordModifiers
{
    None = 0,

    /// <summary>⌘ on macOS, Ctrl on Linux.</summary>
    Primary = 1,
    Shift = 2,
    Alt = 4,

    /// <summary>The physical Control key (⌃ on macOS).</summary>
    Control = 8,
}
