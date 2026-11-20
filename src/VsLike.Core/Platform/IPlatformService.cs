namespace VsLike.Core.Platform;

/// <summary>
/// Kapselt Unterschiede zwischen macOS und Linux (Pfade, Modifier-Tasten, Menüleiste).
/// </summary>
public interface IPlatformService
{
    PlatformKind Kind { get; }

    /// <summary>Gets the directory for user settings, e.g. ~/Library/Application Support/VsLike.</summary>
    string SettingsDirectory { get; }

    /// <summary>Gets a value indicating whether the platform uses a global native menu bar.</summary>
    bool HasNativeMenuBar { get; }

    /// <summary>Gets the display name of the primary command modifier ("⌘" or "Ctrl").</summary>
    string PrimaryModifierDisplay { get; }
}
