namespace VsLike.Core.Settings;

/// <summary>Globale Benutzereinstellungen (settings.json im Einstellungsordner).</summary>
public sealed record AppSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>Gets the UI culture ("de", "en" or empty for system).</summary>
    public string Language { get; init; } = string.Empty;

    public bool StyleCopEnabledByDefault { get; init; }

    public string EditorFontFamily { get; init; } = "JetBrains Mono, Menlo, DejaVu Sans Mono, monospace";

    public double EditorFontSize { get; init; } = 13;

    public IReadOnlyList<string> RecentSolutions { get; init; } = [];

    /// <summary>Gets user key bindings: command id → chord ("Cmd+Shift+P"), empty string unbinds.</summary>
    public IReadOnlyDictionary<string, string> KeyBindings { get; init; } = new Dictionary<string, string>();

    public AppSettings WithRecentSolution(string path, int max = 10)
        => this with { RecentSolutions = [path, .. RecentSolutions.Where(p => !string.Equals(p, path, StringComparison.Ordinal)).Take(max - 1)] };
}
