namespace VsLike.Kit;

/// <summary>
/// The startup hook (<c>engine/src/VsLike.DebugStartupHook</c>) that holds a program debugged in the terminal before
/// <c>Main</c> until the debugger has attached and set the breakpoints.
/// </summary>
public static class DebugStartupHook
{
    public const string ReadyFileVariable = "VSLIKE_DEBUG_READY_FILE";
    public const string FileName = "VsLike.DebugStartupHook.dll";

    /// <summary><c>VSLIKE_DEBUG_HOOK</c>, the install folder (<c>debughook/</c>), or the build of this repository.</summary>
    public static string? Locate()
    {
        var candidates = new List<string>();
        if (System.Environment.GetEnvironmentVariable("VSLIKE_DEBUG_HOOK") is { } overridePath)
        {
            candidates.Add(overridePath);
        }

        candidates.Add(Path.Combine(AppPaths.InstallFolder, "debughook", FileName));
        if (AppPaths.RepositoryRoot is { } repository)
        {
            candidates.Add(Path.Combine(repository, "engine/artifacts.nosync/bin/VsLike.DebugStartupHook/debug", FileName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The program's environment with the hook added (after any hooks the launch profile already sets).</summary>
    public static IReadOnlyDictionary<string, string> Environment(IReadOnlyDictionary<string, string> baseEnvironment, string hook, string readyFile)
    {
        var environment = new Dictionary<string, string>(baseEnvironment);
        var existing = environment.TryGetValue("DOTNET_STARTUP_HOOKS", out var hooks) && hooks.Length > 0 ? new[] { hooks } : [];
        environment["DOTNET_STARTUP_HOOKS"] = string.Join(":", existing.Append(hook));
        environment[ReadyFileVariable] = readyFile;
        return environment;
    }
}
