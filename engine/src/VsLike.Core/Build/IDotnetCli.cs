namespace VsLike.Core.Build;

/// <summary>Startet das dotnet-CLI des Nutzers (build, run, test, restore, new, add package …).</summary>
public interface IDotnetCli
{
    /// <param name="onOutput">Receives stdout and stderr lines as they arrive (background thread).</param>
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<OutputLine>? onOutput, CancellationToken cancellationToken);
}
