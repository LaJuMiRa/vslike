using VsLike.Core.Build;

namespace VsLike.Roslyn.Analyzers;

/// <summary>
/// Die feste StyleCop.Analyzers-Version im NuGet-Cache des Nutzers. Fehlt sie, lädt ein <c>dotnet restore</c> mit
/// <c>PackageDownload</c> sie herunter (respektiert die NuGet-Konfiguration des Nutzers; csproj bleibt unverändert).
/// </summary>
public static class StyleCopPackage
{
    public const string Id = "StyleCop.Analyzers";
    public const string Version = "1.3.180";

    public static string PackagesRoot => Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public static string AnalyzerDirectory => Path.Combine(PackagesRoot, Id.ToLowerInvariant(), Version, "analyzers", "dotnet", "cs");

    public static string AnalyzerAssembly => Path.Combine(AnalyzerDirectory, "StyleCop.Analyzers.dll");

    public static string CodeFixAssembly => Path.Combine(AnalyzerDirectory, "StyleCop.Analyzers.CodeFixes.dll");

    public static bool IsInCache => File.Exists(AnalyzerAssembly) && File.Exists(CodeFixAssembly);

    public static async Task<bool> EnsureInCacheAsync(IDotnetCli cli, CancellationToken cancellationToken)
    {
        if (IsInCache)
        {
            return true;
        }

        var directory = Path.Combine(Path.GetTempPath(), "vslike-stylecop-download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Download.csproj");
            await File.WriteAllTextAsync(
                project,
                $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup>\n  <ItemGroup><PackageDownload Include=\"{Id}\" Version=\"[{Version}]\" /></ItemGroup>\n</Project>\n",
                cancellationToken).ConfigureAwait(false);
            await cli.RunAsync(["restore", project], directory, null, cancellationToken).ConfigureAwait(false);
            return IsInCache;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
