namespace VsLike.Core.Settings;

/// <summary>
/// Einstellungen pro Solution in <c>.vslike/settings.json</c> neben der Solution-Datei.
/// Pfade sind relativ zum Solution-Ordner, damit die Datei mit dem Repository wandern kann.
/// </summary>
public sealed record SolutionSettings
{
    public const string FolderName = ".vslike";

    public string? StartupProject { get; init; }

    public string Configuration { get; init; } = "Debug";

    public string? LaunchProfile { get; init; }

    /// <summary>Gets the StyleCop switch for this solution; null means "use the global default".</summary>
    public bool? StyleCopEnabled { get; init; }

    public static string PathFor(string solutionPath) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(solutionPath)!, FolderName, "settings.json");

    public static SolutionSettings Load(string solutionPath) => new JsonFileStore<SolutionSettings>(PathFor(solutionPath)).Load();

    public void Save(string solutionPath) => new JsonFileStore<SolutionSettings>(PathFor(solutionPath)).Save(this);
}
