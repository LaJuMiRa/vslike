using System.Diagnostics;

namespace VsLike.Core.Build;

public sealed class DotnetCli : IDotnetCli
{
    private readonly string _dotnet;

    public DotnetCli(string? dotnetPath = null)
    {
        _dotnet = dotnetPath ?? LocateDotnet();
    }

    public static string LocateDotnet()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root)
        {
            candidates.Add(Path.Combine(root, "dotnet"));
        }

        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p, "dotnet")));

        // GUI-Apps auf macOS erben nicht den PATH der Shell.
        candidates.AddRange(["/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/bin/dotnet", "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet")]);
        return candidates.FirstOrDefault(File.Exists) ?? "dotnet";
    }

    /// <summary>
    /// Builds a start info without the MSBuild variables the IDE process carries after MSBuildLocator registration
    /// or when running under a test host; child builds must discover their SDK on their own.
    /// </summary>
    public ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var start = new ProcessStartInfo(_dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var key in start.Environment.Keys.Where(IsInheritedBuildVariable).ToList())
        {
            start.Environment.Remove(key);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return start;
    }

    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
    {
        var lines = new List<OutputLine>();
        var gate = new object();
        using var process = new Process { StartInfo = CreateStartInfo(arguments, workingDirectory), EnableRaisingEvents = true };
        void Receive(string? data, bool isError)
        {
            if (data is null)
            {
                return;
            }

            var line = new OutputLine(data, isError);
            lock (gate)
            {
                lines.Add(line);
            }

            onOutput?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Receive(e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => Receive(e.Data, isError: true);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        lock (gate)
        {
            return new ProcessResult(cancelled ? -1 : process.ExitCode, [.. lines], cancelled);
        }
    }

    internal static bool IsInheritedBuildVariable(string key)
        => key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase)
            || key is "DOTNET_HOST_PATH" or "MSBUILD_EXE_PATH";
}
