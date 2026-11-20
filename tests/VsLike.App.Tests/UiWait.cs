using Avalonia.Headless;
using Avalonia.Threading;

namespace VsLike.App.Tests;

internal static class UiWait
{
    /// <summary>Polls on the UI thread (letting posted work run) until the condition holds.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting: {because}");
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
    }
}
