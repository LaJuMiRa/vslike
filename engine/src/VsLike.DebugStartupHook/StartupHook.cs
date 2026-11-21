using System;
using System.IO;
using System.Threading;

/// <summary>
/// Runs before <c>Main</c> of a program that VsLike debugs in its terminal. The program waits until the debugger
/// is attached and the breakpoints are set (VsLike then creates the file named in
/// <c>VSLIKE_DEBUG_READY_FILE</c>), so that breakpoints at the very start of <c>Main</c> are hit. Programs started
/// without that variable are not affected.
/// </summary>
internal static class StartupHook
{
    private const string ReadyFileVariable = "VSLIKE_DEBUG_READY_FILE";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Called by the runtime (name and signature are fixed by the startup hook contract).</summary>
    public static void Initialize()
    {
        var readyFile = Environment.GetEnvironmentVariable(ReadyFileVariable);
        if (string.IsNullOrEmpty(readyFile))
        {
            return;
        }

        // Child processes of the program must not wait as well.
        Environment.SetEnvironmentVariable(ReadyFileVariable, null);
        var deadline = DateTime.UtcNow + Timeout;
        while (!File.Exists(readyFile) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
    }
}
