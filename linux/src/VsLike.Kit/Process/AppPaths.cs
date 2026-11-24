namespace VsLike.Kit;

/// <summary>
/// Linux locations that replace the macOS bundle and Library folders (no Swift original): the install folder of the
/// app (<c>/usr/lib/vslike</c> in the packages), the XDG cache and config folders, and the repository for development builds.
/// </summary>
public static class AppPaths
{
    /// <summary>Gets the folder that holds <c>vslike.dll</c>, <c>engine/</c>, <c>debughook/</c> and <c>netcoredbg/</c>.</summary>
    public static string InstallFolder => AppContext.BaseDirectory.TrimEnd('/');

    /// <summary>Gets <c>$XDG_CACHE_HOME/vslike</c> (default <c>~/.cache/vslike</c>).</summary>
    public static string CacheFolder => Path.Combine(XdgFolder("XDG_CACHE_HOME", ".cache"), "vslike");

    /// <summary>Gets <c>$XDG_CONFIG_HOME/vslike</c> (default <c>~/.config/vslike</c>).</summary>
    public static string ConfigFolder => Path.Combine(XdgFolder("XDG_CONFIG_HOME", ".config"), "vslike");

    /// <summary>
    /// Gets the repository root (the folder with <c>engine/VsLike.Engine.slnx</c>) above the running assembly, or null
    /// in an installed app. Replaces the <c>#filePath</c> lookups of the Swift original.
    /// </summary>
    public static string? RepositoryRoot
    {
        get
        {
            var folder = new DirectoryInfo(InstallFolder);
            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "engine", "VsLike.Engine.slnx")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            return null;
        }
    }

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string XdgFolder(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) || !Path.IsPathRooted(value) ? Path.Combine(Home, fallback) : value;
    }
}
