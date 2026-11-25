using System.Diagnostics;

namespace VsLike.Desktop.Editor;

/// <summary>
/// Fire-and-forget work of the editor on the UI thread (Swift's <c>Task { }</c> with <c>try?</c>): cancellations end
/// quietly, other failures (engine errors, a closed engine) are traced instead of crashing the app.
/// </summary>
internal static class EditorTasks
{
    public static async Task Run(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.TraceWarning($"[editor] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Cancels the previous request of a kind and starts a new token for the next one.</summary>
    public static CancellationToken Renew(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source = new CancellationTokenSource();
        return source.Token;
    }
}
