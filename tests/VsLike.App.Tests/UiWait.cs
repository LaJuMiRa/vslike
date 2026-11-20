using Avalonia.Headless;
using Avalonia.Threading;

namespace VsLike.App.Tests;

internal static class UiWait
{
    private static readonly List<string> Unhandled = [];

    /// <summary>Records exceptions that escaped to the dispatcher, so a timeout can report what broke the UI.</summary>
    public static void Record(string source, Exception? exception)
    {
        lock (Unhandled)
        {
            Unhandled.Add($"[{source}] {exception}");
        }
    }

    /// <summary>Polls on the UI thread (letting posted work run) until the condition holds.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                string recorded;
                lock (Unhandled)
                {
                    recorded = string.Join("\n", Unhandled);
                }

                throw new TimeoutException($"Timed out waiting: {because}\nUnhandled so far:\n{recorded}");
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
    }
}
