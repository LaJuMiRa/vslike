namespace VsLike.Core.Platform;

public sealed class PlatformService : IPlatformService
{
    private const string AppFolderName = "VsLike";

    public PlatformService()
        : this(DetectKind(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal PlatformService(PlatformKind kind, string homeDirectory)
    {
        Kind = kind;
        SettingsDirectory = kind switch
        {
            PlatformKind.MacOS => Path.Combine(homeDirectory, "Library", "Application Support", AppFolderName),
            _ => Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(homeDirectory, ".config"),
                AppFolderName.ToLowerInvariant()),
        };
    }

    public PlatformKind Kind { get; }

    public string SettingsDirectory { get; }

    public bool HasNativeMenuBar => Kind == PlatformKind.MacOS;

    public string PrimaryModifierDisplay => Kind == PlatformKind.MacOS ? "⌘" : "Ctrl";

    private static PlatformKind DetectKind()
    {
        if (OperatingSystem.IsMacOS())
        {
            return PlatformKind.MacOS;
        }

        return OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Other;
    }
}
