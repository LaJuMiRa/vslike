namespace VsLike.Core.Layout;

/// <summary>
/// Persistierter Zustand des Dock-Layouts: welches Panel in welchem Bereich liegt, Größen und Sichtbarkeit.
/// </summary>
public sealed record LayoutState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<PanelPlacement> Panels { get; init; } = [];

    /// <summary>Gets the proportions of the areas (area id → 0..1).</summary>
    public IReadOnlyDictionary<string, double> Proportions { get; init; } = new Dictionary<string, double>();

    public double WindowWidth { get; init; } = 1400;

    public double WindowHeight { get; init; } = 900;

    public bool IsMaximized { get; init; }
}
