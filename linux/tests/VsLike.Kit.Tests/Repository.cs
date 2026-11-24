using VsLike.Kit;

namespace VsLike.Kit.Tests;

/// <summary>Paths of the repository for tests that run against the real .NET SDK, engine and debugger.</summary>
internal static class Repository
{
    public static string Root => AppPaths.RepositoryRoot ?? throw new InvalidOperationException("Run the tests from the repository build.");

    public static string EngineDll => Environment.GetEnvironmentVariable("VSLIKE_ENGINE_DLL") is { Length: > 0 } overridePath
        ? overridePath
        : Path.Combine(Root, "engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll");

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string TemporaryFolder(string prefix) => Path.Combine(RealPath.Resolve(Path.GetTempPath()), $"{prefix}-{Guid.NewGuid()}");

    /// <summary>
    /// netcoredbg for the debugger tests. On a Mac without the Linux cache the tests use the copy that the macOS
    /// build.sh caches (test helper only; the product looks in the install folder and the XDG cache).
    /// </summary>
    public static string? Adapter()
    {
        if (DebugSession.LocateAdapter() is { } adapter)
        {
            return adapter;
        }

        var mac = Path.Combine(AppPaths.Home, "Library/Caches/VsLike/netcoredbg/3.2.0-1092/netcoredbg/netcoredbg");
        if (!File.Exists(mac))
        {
            return null;
        }

        Environment.SetEnvironmentVariable("VSLIKE_NETCOREDBG", mac);
        return DebugSession.LocateAdapter();
    }

    /// <summary>Copies a sample solution from engine/tests/TestAssets into a fresh temporary folder (symlinks resolved).</summary>
    public static string CopySample(string name)
    {
        var target = TemporaryFolder("vslike-linux");
        CopyDirectory(Path.Combine(Root, "engine/tests/TestAssets", name), target);
        return target;
    }

    public static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(folder);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(folder, Path.Combine(target, name));
        }
    }
}
