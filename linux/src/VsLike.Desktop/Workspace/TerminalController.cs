using CommunityToolkit.Mvvm.ComponentModel;
using Porta.Pty;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// One terminal tab: an interactive login shell or a program started by Run. The session owns the process and its
/// pseudo terminal (<see cref="Connection"/>, Porta.Pty); the terminal view (Iciclecreek <c>TerminalControl</c>) attaches to it
/// with <c>AttachConnection(session.Connection)</c> once its template is applied, and reports title changes through
/// <see cref="Title"/>. Output written before the view attaches waits in the pseudo terminal.
/// </summary>
public sealed partial class TerminalSession : ObservableObject
{
    // Colours of the macOS terminal (applyColors); the view uses them for background, text, caret and selection.
    public const uint BackgroundDark = 0x15171B;
    public const uint BackgroundLight = 0xFBFBFC;
    public const uint ForegroundDark = 0xDCDFE6;
    public const uint ForegroundLight = 0x1F2329;

    internal TerminalSession(string title, bool isRun, IPtyConnection connection)
    {
        Title = title;
        IsRun = isRun;
        Connection = connection;
        connection.ProcessExited += (_, e) => Workspace.Post(() => Finished(e.ExitCode));
    }

    public Guid Id { get; } = Guid.NewGuid();

    public bool IsRun { get; }

    public IPtyConnection Connection { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; } = true;

    [ObservableProperty]
    public partial int? ExitCode { get; private set; }

    /// <summary>Gets the process ID of the program.</summary>
    public int ProcessId => Connection.Pid;

    /// <summary>WIFEXITED / WEXITSTATUS of a <c>waitpid</c> status; a signal ends the program with 128 + signal, as in a shell.</summary>
    public static int ExitCodeFromStatus(int status) => (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);

    public void Terminate()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            Connection.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private void Finished(int code)
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        ExitCode = code;
    }
}

/// <summary>The terminal tabs of the bottom panel.</summary>
public sealed partial class TerminalController : ObservableObject
{
    private readonly Workspace _workspace;

    public TerminalController(Workspace workspace)
    {
        _workspace = workspace;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Active), nameof(HasRunningProgram))]
    public partial IReadOnlyList<TerminalSession> Sessions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Active))]
    public partial Guid? ActiveId { get; set; }

    public TerminalSession? Active => Sessions.FirstOrDefault(s => s.Id == ActiveId);

    public bool HasRunningProgram => Sessions.Any(s => s.IsRun && s.IsRunning);

    /// <summary>Gets the terminal font size (one point below the editor, at least 11).</summary>
    public double TerminalFontSize => Math.Max(11, _workspace.Preferences.FontSize - 1);

    private string WorkingDirectory => _workspace.RootPath ?? AppPaths.Home;

    /// <summary>Opens a login shell in the solution folder (<c>$SHELL</c>, else <c>/bin/bash</c>).</summary>
    public async Task<TerminalSession?> NewShell()
    {
        var shell = System.Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } value ? value : "/bin/bash";
        var session = await Spawn(Path.GetFileName(shell), isRun: false, shell, ["-l"], WorkingDirectory, ChildEnvironment(null));
        return session;
    }

    /// <summary>Starts <c>dotnet &lt;arguments&gt;</c> in a new tab (replacing a finished run tab with the same title).</summary>
    public async Task<TerminalSession?> Run(string title, IReadOnlyList<string> arguments, string directory, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (DotnetLocator.Locate() is not { } dotnet)
        {
            _workspace.Alert = new AlertMessage(title, CommandException.DotnetNotFound().Message);
            return null;
        }

        foreach (var old in Sessions.Where(s => s.IsRun && !s.IsRunning && s.Title == title).ToList())
        {
            Close(old);
        }

        return await Spawn(title, isRun: true, dotnet, arguments, directory, ChildEnvironment(environment));
    }

    public void Close(TerminalSession session)
    {
        session.Terminate();
        Sessions = [.. Sessions.Where(s => s.Id != session.Id)];
        if (ActiveId == session.Id)
        {
            ActiveId = Sessions.LastOrDefault()?.Id;
        }
    }

    public void StopRuns()
    {
        foreach (var session in Sessions.Where(s => s.IsRun && s.IsRunning))
        {
            session.Terminate();
        }
    }

    private async Task<TerminalSession?> Spawn(string title, bool isRun, string app, IReadOnlyList<string> arguments, string directory, Dictionary<string, string> environment)
    {
        try
        {
            var connection = await PtyProvider.SpawnAsync(
                new PtyOptions
                {
                    Name = title,
                    Cols = 120,
                    Rows = 30,
                    Cwd = directory,
                    App = app,
                    CommandLine = [.. arguments],
                    Environment = environment,
                },
                CancellationToken.None);
            var session = new TerminalSession(title, isRun, connection);
            Sessions = [.. Sessions, session];
            ActiveId = session.Id;
            _workspace.BottomTab = BottomTab.Terminal;
            _workspace.IsBottomPanelVisible = true;
            return session;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException)
        {
            _workspace.Alert = new AlertMessage(title, ex.Message);
            return null;
        }
    }

    private static Dictionary<string, string> ChildEnvironment(IReadOnlyDictionary<string, string>? extra)
    {
        Dictionary<string, string> environment;
        try
        {
            environment = CommandRunner.Dotnet().ChildEnvironment();
        }
        catch (CommandException)
        {
            environment = DotnetLocator.CurrentEnvironmentCopy();
        }

        environment["TERM"] = "xterm-256color";
        environment["COLORTERM"] = "truecolor";
        environment["TERM_PROGRAM"] = "VsLike";
        environment.TryAdd("LANG", "en_US.UTF-8");
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            environment[key] = value;
        }

        return environment;
    }
}
