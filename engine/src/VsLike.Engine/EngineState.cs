using System.Collections.Concurrent;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Roslyn;

namespace VsLike.Engine;

/// <summary>
/// Everything one engine process holds: the Roslyn workspace, its language features and the handle tables that let the
/// app refer to completion entries and code actions it received earlier.
/// </summary>
public sealed class EngineState : IDisposable
{
    private int _nextHandle;

    public EngineState(IDotnetCli cli)
    {
        Cli = cli;
        Features = new RoslynLanguageFeatures(Workspace);
    }

    public RoslynWorkspace Workspace { get; } = new();

    public RoslynLanguageFeatures Features { get; }

    public IDotnetCli Cli { get; }

    /// <summary>Gets the entries of the latest completion list (replaced by every new list).</summary>
    public ConcurrentDictionary<int, CompletionEntry> Completions { get; } = new();

    /// <summary>Gets the actions of the latest code action query (replaced by every new query).</summary>
    public ConcurrentDictionary<int, CodeActionInfo> CodeActions { get; } = new();

    public int NextHandle() => Interlocked.Increment(ref _nextHandle);

    public void Dispose() => Workspace.Dispose();
}
