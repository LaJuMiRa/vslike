using System.Diagnostics;
using System.Text;

namespace VsLike.Kit;

/// <summary>One line of process output; <see cref="IsError"/> marks lines from stderr.</summary>
public sealed record OutputLine(string Text, bool IsError = false);

public sealed record ProcessResult(int ExitCode, IReadOnlyList<OutputLine> Output, bool WasCancelled)
{
    public bool Succeeded => ExitCode == 0 && !WasCancelled;

    public string StandardOutput => string.Join("\n", Output.Where(l => !l.IsError).Select(l => l.Text));

    public string AllText => string.Join("\n", Output.Select(l => l.Text));
}

public class CommandException : Exception
{
    public CommandException(string message)
        : base(message)
    {
    }

    public static CommandException DotnetNotFound() =>
        new DotnetNotFoundException("The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download.");

    public static CommandException Failed(string command, int exitCode, string output)
    {
        var tail = string.Join("\n", output.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(15));
        return new CommandException($"{command} failed (exit {exitCode}).\n{tail}");
    }
}

public sealed class DotnetNotFoundException(string message) : CommandException(message);

/// <summary>
/// Runs a command line tool (normally <c>dotnet</c>) with live output, line by line, and cancellation through a
/// <see cref="CancellationToken"/>.
/// </summary>
public sealed record CommandRunner(string Executable, IReadOnlyDictionary<string, string> Environment)
{
    public CommandRunner(string executable)
        : this(executable, new Dictionary<string, string>())
    {
    }

    /// <summary>The <c>dotnet</c> CLI with telemetry and logo off and without MSBuild variables inherited from a test host.</summary>
    public static CommandRunner Dotnet(string? language = null)
    {
        var path = DotnetLocator.Locate() ?? throw CommandException.DotnetNotFound();
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        };
        if (language is not null)
        {
            environment["DOTNET_CLI_UI_LANGUAGE"] = language;
        }

        return new CommandRunner(path, environment);
    }

    /// <summary>
    /// Environment for child processes: the current one without inherited build variables, plus <see cref="Environment"/>,
    /// plus <paramref name="extra"/>, plus the dotnet folder on <c>PATH</c> (apps started from a desktop launcher get a minimal <c>PATH</c>).
    /// </summary>
    public Dictionary<string, string> ChildEnvironment(IReadOnlyDictionary<string, string>? extra = null)
    {
        var result = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("msbuild", StringComparison.OrdinalIgnoreCase) || key.StartsWith("VSTEST", StringComparison.Ordinal) || key == "DOTNET_HOST_PATH")
            {
                continue;
            }

            result[key] = (string?)entry.Value ?? string.Empty;
        }

        foreach (var (key, value) in Environment)
        {
            result[key] = value;
        }

        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            result[key] = value;
        }

        var folder = System.IO.Path.GetDirectoryName(Executable) ?? string.Empty;
        var path = result.GetValueOrDefault("PATH") ?? "/usr/bin:/bin:/usr/sbin:/sbin";
        if (!path.Split(':').Contains(folder))
        {
            result["PATH"] = folder + ":" + path;
        }

        return result;
    }

    public async Task<ProcessResult> Run(
        IReadOnlyList<string> arguments,
        string? directory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        Action<OutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        foreach (var (key, value) in ChildEnvironment(environment))
        {
            info.Environment[key] = value;
        }

        if (directory is not null)
        {
            info.WorkingDirectory = directory;
        }

        var collector = new OutputCollector(onOutput);
        using var process = new Process { StartInfo = info };
        process.Start();
        process.StandardInput.Close();
        var stdout = Pump(process.StandardOutput, false, collector);
        var stderr = Pump(process.StandardError, true, collector);
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, collector.Lines, cancelled);
    }

    /// <summary>Runs and throws <see cref="CommandException"/> when the exit code is not 0.</summary>
    public async Task<ProcessResult> RunChecked(IReadOnlyList<string> arguments, string? directory = null, CancellationToken cancellationToken = default)
    {
        var result = await Run(arguments, directory, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw CommandException.Failed("dotnet " + string.Join(" ", arguments), result.ExitCode, result.AllText);
        }

        return result;
    }

    internal static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // SIGTERM-like: kill the tree so that `dotnet run` children end too.
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private static async Task Pump(StreamReader reader, bool isError, OutputCollector collector)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            collector.Receive(line.TrimEnd('\r'), isError);
        }
    }
}

/// <summary>Thread-safe collector of the lines of both pipes of a process.</summary>
internal sealed class OutputCollector(Action<OutputLine>? onOutput)
{
    private readonly Lock _lock = new();
    private readonly List<OutputLine> _collected = [];

    public IReadOnlyList<OutputLine> Lines
    {
        get
        {
            lock (_lock)
            {
                return [.. _collected];
            }
        }
    }

    public void Receive(string text, bool isError)
    {
        var line = new OutputLine(text, isError);
        lock (_lock)
        {
            _collected.Add(line);
        }

        onOutput?.Invoke(line);
    }
}
