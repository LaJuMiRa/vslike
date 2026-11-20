namespace VsLike.Projects.Build;

/// <summary>Ein Startprofil aus <c>Properties/launchSettings.json</c>.</summary>
public sealed record LaunchProfile(
    string Name,
    string CommandName,
    string? CommandLineArgs,
    string? WorkingDirectory,
    string? ApplicationUrl,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    /// <summary>Gets a value indicating whether <c>dotnet run</c> can start the profile (IIS/Docker profiles cannot).</summary>
    public bool IsRunnable => string.Equals(CommandName, "Project", StringComparison.OrdinalIgnoreCase)
        || string.Equals(CommandName, "Executable", StringComparison.OrdinalIgnoreCase);
}
