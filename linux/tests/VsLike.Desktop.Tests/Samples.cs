using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop.Tests;

/// <summary>Copies a sample from engine/tests/TestAssets into a temporary folder and restores it.</summary>
internal static class Samples
{
    public static string Repository => AppPaths.RepositoryRoot ?? throw new InvalidOperationException("Run the tests from the repository build.");

    public static async Task<string> Copy(string name)
    {
        var target = Path.Combine(RealPath.Resolve(Path.GetTempPath()), $"vslike-app-{Guid.NewGuid()}");
        CopyDirectory(Path.Combine(Repository, "engine/tests/TestAssets", name), target);
        var solution = Directory.GetFiles(target, "*.slnx")[0];
        await CommandRunner.Dotnet("en").RunChecked(["restore", solution]);
        return target;
    }

    /// <summary>Polls until the condition holds or the timeout passes, keeping the UI thread running.</summary>
    public static async Task<bool> Wait(Func<bool> condition, double seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }

        return condition();
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

    /// <summary>netcoredbg on a Mac without the Linux cache: the copy cached by the macOS build.sh (test helper only).</summary>
    public static void UseMacAdapterIfNeeded()
    {
        var mac = Path.Combine(AppPaths.Home, "Library/Caches/VsLike/netcoredbg/3.2.0-1092/netcoredbg/netcoredbg");
        if (DebugSession.LocateAdapter() is null && File.Exists(mac))
        {
            Environment.SetEnvironmentVariable("VSLIKE_NETCOREDBG", mac);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.GetDirectories(source).Where(f => Path.GetFileName(f) is not ("bin" or "obj")))
        {
            CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }
}
