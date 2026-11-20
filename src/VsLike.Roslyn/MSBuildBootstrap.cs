using Microsoft.Build.Locator;

namespace VsLike.Roslyn;

/// <summary>Registriert genau einmal die MSBuild-Instanz des installierten .NET SDK (höchste Version).</summary>
public static class MSBuildBootstrap
{
    private static readonly object Gate = new();
    private static VisualStudioInstance? _instance;

    /// <summary>Gets the registered SDK, or null if none was registered yet.</summary>
    public static VisualStudioInstance? Instance => _instance;

    public static VisualStudioInstance EnsureRegistered()
    {
        lock (Gate)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            if (MSBuildLocator.IsRegistered)
            {
                throw new InvalidOperationException("MSBuild was registered outside of MSBuildBootstrap.");
            }

            var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions { DiscoveryTypes = DiscoveryType.DotNetSdk })
                .OrderByDescending(i => i.Version)
                .FirstOrDefault() ?? throw new InvalidOperationException("No .NET SDK found. Install the .NET SDK 10 or newer.");
            MSBuildLocator.RegisterInstance(instance);
            _instance = instance;
            return instance;
        }
    }
}
