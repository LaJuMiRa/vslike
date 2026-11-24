using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>A variable in the Variables or Watch list; children load when expanded.</summary>
public sealed record VariableNode(string Id, string Name, string Value, string? Type, int Reference, IReadOnlyList<VariableNode>? Children = null);

public sealed record WatchExpression(string Expression, string Value = "", string? Type = null, bool IsError = false)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>The debugger: builds, starts netcoredbg, keeps breakpoints in sync and shows where the program stopped.</summary>
public sealed partial class DebugController : ObservableObject
{
    private readonly Workspace _workspace;
    private DebugSession? _session;
    private CancellationTokenSource? _eventTask;

    /// <summary>The terminal tab the debugged program runs in, and the file that releases it from the startup hook.</summary>
    private TerminalSession? _terminalSession;
    private string? _readyFile;
    private int _consoleId;

    public DebugController(Workspace workspace)
    {
        _workspace = workspace;
    }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial bool IsStarting { get; private set; }

    [ObservableProperty]
    public partial bool IsStopped { get; private set; }

    [ObservableProperty]
    public partial string? StopReason { get; private set; }

    [ObservableProperty]
    public partial int ThreadId { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DebugThread> Threads { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFrame))]
    public partial IReadOnlyList<StackFrame> Frames { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFrame))]
    public partial int? SelectedFrameId { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VariableNode> Variables { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<WatchExpression> Watches { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<OutputLog.Line> Console { get; private set; } = [];

    public StackFrame? SelectedFrame => Frames.FirstOrDefault(f => f.Id == SelectedFrameId);

    public async Task Start()
    {
        if (IsActive || IsStarting)
        {
            if (IsStopped)
            {
                await Resume();
            }

            return;
        }

        if (_workspace.Build.StartupProject is not { } project)
        {
            _workspace.Alert = new AlertMessage(L.T("Debug"), L.T("This solution has no project that can be started."));
            return;
        }

        if (DebugSession.LocateAdapter() is not { } adapter)
        {
            _workspace.Alert = new AlertMessage(L.T("Debug"), DebugException.AdapterNotFound().Message);
            return;
        }

        IsStarting = true;
        try
        {
            if (await _workspace.Build.Run(BuildKind.Build) is not { Succeeded: true })
            {
                return;
            }

            await Launch(project, adapter);
        }
        catch (Exception ex) when (ex is DebugException or CommandException or IOException or System.ComponentModel.Win32Exception)
        {
            _workspace.Alert = new AlertMessage(L.T("Debug"), ex.Message);
        }
        finally
        {
            IsStarting = false;
        }
    }

    /// <summary>Children of an expandable variable.</summary>
    public async Task<IReadOnlyList<VariableNode>> ChildrenOf(VariableNode variable)
    {
        if (_session is not { } session || variable.Reference <= 0)
        {
            return [];
        }

        try
        {
            return [.. (await session.Variables(variable.Reference)).Select(v => Node(v, variable.Id))];
        }
        catch (DebugException)
        {
            return [];
        }
    }

    public async Task RefreshWatches()
    {
        if (_session is not { } session)
        {
            return;
        }

        var updated = new List<WatchExpression>();
        foreach (var watch in Watches)
        {
            try
            {
                var result = await session.Evaluate(watch.Expression, SelectedFrameId, "watch");
                updated.Add(watch with { Value = result.Value, Type = result.Type, IsError = false });
            }
            catch (DebugException ex)
            {
                updated.Add(watch with { Value = ex.Message, IsError = true });
            }
        }

        Watches = updated;
    }

    public void AddWatch(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        Watches = [.. Watches, new WatchExpression(trimmed)];
        _ = RefreshWatches();
    }

    /// <summary>Evaluates in the console (REPL).</summary>
    public async Task Evaluate(string expression)
    {
        AppendConsole("› " + expression);
        if (_session is not { } session || !IsStopped)
        {
            AppendConsole(L.T("The program is not paused."), isError: true);
            return;
        }

        try
        {
            var result = await session.Evaluate(expression, SelectedFrameId, "repl");
            AppendConsole(result.Value + (result.Type is null ? string.Empty : $"  ({result.Type})"));
        }
        catch (DebugException ex)
        {
            AppendConsole(ex.Message, isError: true);
        }
    }

    /// <summary>Value for hovering an identifier while paused.</summary>
    public async Task<string?> HoverValue(string expression)
    {
        if (_session is not { } session || !IsStopped || expression.Length == 0)
        {
            return null;
        }

        try
        {
            var result = await session.Evaluate(expression, SelectedFrameId, "hover");
            return result.Type is null ? $"{expression} = {result.Value}" : $"{expression} = {result.Value}  ({result.Type})";
        }
        catch (DebugException)
        {
            return null;
        }
    }

    /// <summary>Continues; the terminal comes to the front again, as the program may wait for input.</summary>
    public Task Resume()
    {
        if (_terminalSession?.IsRunning == true)
        {
            _workspace.BottomTab = BottomTab.Terminal;
            _workspace.Terminals.ActiveId = _terminalSession.Id;
        }

        return RunCommand((s, t) => s.Resume(t));
    }

    public Task StepOver() => RunCommand((s, t) => s.StepOver(t));

    public Task StepIn() => RunCommand((s, t) => s.StepIn(t));

    public Task StepOut() => RunCommand((s, t) => s.StepOut(t));

    public async Task Pause()
    {
        if (_session is not { } session || IsStopped)
        {
            return;
        }

        try
        {
            var thread = ThreadId != 0 ? ThreadId : (await session.Threads()).FirstOrDefault()?.Id ?? 0;
            await session.Pause(thread);
        }
        catch (DebugException)
        {
            // try? in the original.
        }
    }

    public async Task Stop()
    {
        if (_session is not { } session)
        {
            return;
        }

        await session.Stop();
        _terminalSession?.Terminate();
        Finish();
    }

    /// <summary>Sends the breakpoints of a file to the running session.</summary>
    public void BreakpointsChanged(CodeDocument document)
    {
        _workspace.PersistBreakpoints();
        if (_session is not { } session || !IsActive)
        {
            return;
        }

        var list = document.Breakpoints.Values.OrderBy(b => b.Line).ToList();
        _ = Workspace.Run(() => session.SetBreakpoints(document.Path, list));
    }

    private static VariableNode Node(DebugVariable variable, string parent) =>
        new(parent + "/" + variable.Name, variable.Name, variable.Value, variable.Type, variable.VariablesReference, variable.VariablesReference > 0 ? [] : null);

    private async Task Launch(string project, string adapter)
    {
        var runner = CommandRunner.Dotnet(_workspace.Preferences.EffectiveLanguage);
        var configuration = _workspace.Build.Configuration;
        if (await new BuildService(runner).TargetPath(project, configuration) is not { } program)
        {
            _workspace.Alert = new AlertMessage(L.T("Debug"), L.T("The built program was not found."));
            return;
        }

        var profile = _workspace.Build.Profiles.FirstOrDefault(p => p.Name == _workspace.Build.LaunchProfile);
        var arguments = profile?.CommandLineArgs?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var environment = new Dictionary<string, string>(profile?.Environment ?? new Dictionary<string, string>());
        if (profile?.ApplicationURL is { } url)
        {
            environment["ASPNETCORE_URLS"] = url;
        }

        Console = [];

        // The program runs in a terminal tab (so it can read input) and waits in the startup hook until the
        // debugger has attached and set the breakpoints.
        if (DebugStartupHook.Locate() is not { } hook)
        {
            _workspace.Alert = new AlertMessage(L.T("Debug"), L.T("The debug startup hook is missing from the app bundle."));
            return;
        }

        var readyFile = Path.Combine(Path.GetTempPath(), $"vslike-debug-{Guid.NewGuid()}");
        _readyFile = readyFile;
        var terminal = await _workspace.Terminals.Run(
            L.T("Debug: %@", _workspace.Build.StartupProjectName ?? string.Empty),
            [program, .. arguments],
            BuildService.WorkingDirectory(project, profile),
            DebugStartupHook.Environment(environment, hook, readyFile));
        if (terminal is null)
        {
            return;
        }

        _terminalSession = terminal;
        AppendConsole(L.T("Attaching netcoredbg to %@ (process %d)…", Path.GetFileName(program), terminal.ProcessId));
        DebugSession session;
        try
        {
            (session, _) = await DebugSession.Attach(adapter, terminal.ProcessId, _workspace.AllBreakpoints, runner.Executable, readyFile);
        }
        catch
        {
            terminal.Terminate();
            _terminalSession = null;
            RemoveReadyFile();
            throw;
        }

        _session = session;
        IsActive = true;
        _workspace.BottomTab = BottomTab.Terminal;
        _workspace.IsBottomPanelVisible = true;
        _workspace.Status = new StatusMessage(L.T("Debugging %@", _workspace.Build.StartupProjectName ?? string.Empty), StatusStyle.Progress);
        ListenTo(session);
    }

    private void ListenTo(DebugSession session)
    {
        var cancellation = new CancellationTokenSource();
        _eventTask = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var debugEvent in session.Events.ReadAllAsync(cancellation.Token))
                {
                    var done = new TaskCompletionSource();
                    Workspace.Post(async () =>
                    {
                        await Handle(debugEvent);
                        done.SetResult();
                    });
                    await done.Task;
                }
            }
            catch (OperationCanceledException)
            {
                // Finished.
            }
        });
    }

    private async Task Handle(DebugEvent debugEvent)
    {
        switch (debugEvent)
        {
            case DebugEvent.Stopped stopped:
                ThreadId = stopped.ThreadId;
                StopReason = stopped.Description ?? stopped.Reason;
                await LoadStop();
                break;
            case DebugEvent.Continued:
                IsStopped = false;
                ClearExecutionLine();
                break;
            case DebugEvent.Output output when output.Category != "telemetry":
                AppendConsole(output.Text.EndsWith('\n') ? output.Text[..^1] : output.Text, output.Category == "stderr");
                break;
            case DebugEvent.Exited exited:
                AppendConsole(L.T("The program exited with code %lld.", exited.Code));
                break;
            case DebugEvent.Terminated:
                Finish();
                break;
        }
    }

    /// <summary>Loads threads, stack, variables and watches, then marks the session as stopped and shows the line.</summary>
    private async Task LoadStop()
    {
        if (_session is not { } session)
        {
            return;
        }

        Threads = await Try(session.Threads) ?? [];
        Frames = await Try(() => session.StackTrace(ThreadId)) ?? [];
        var top = Frames.FirstOrDefault(f => f.FilePath is not null) ?? Frames.FirstOrDefault();
        SelectedFrameId = top?.Id;
        await LoadFrame();
        IsStopped = true;
        _workspace.BottomTab = BottomTab.Debug;
        _workspace.Status = new StatusMessage(L.T("Paused: %@", StopReason ?? string.Empty), StatusStyle.Warning);
    }

    private async Task LoadFrame()
    {
        if (_session is not { } session || SelectedFrame is not { } frame)
        {
            Variables = [];
            return;
        }

        var nodes = new List<VariableNode>();
        foreach (var scope in await Try(() => session.Scopes(frame.Id)) ?? [])
        {
            foreach (var variable in await Try(() => session.Variables(scope.VariablesReference)) ?? [])
            {
                nodes.Add(Node(variable, scope.Name));
            }
        }

        Variables = nodes;
        await RefreshWatches();
        ShowExecutionLine(frame);
    }

    private async Task RunCommand(Func<DebugSession, int, Task> command)
    {
        if (_session is not { } session || !IsStopped)
        {
            return;
        }

        IsStopped = false;
        ClearExecutionLine();
        _workspace.Status = new StatusMessage(L.T("Running…"), StatusStyle.Progress);
        try
        {
            await command(session, ThreadId);
        }
        catch (DebugException ex)
        {
            AppendConsole(ex.Message, isError: true);
        }
    }

    private void Finish()
    {
        if (!IsActive && _session is null)
        {
            return;
        }

        _eventTask?.Cancel();
        _session = null;
        _terminalSession = null;
        RemoveReadyFile();
        IsActive = false;
        IsStopped = false;
        Frames = [];
        Threads = [];
        Variables = [];
        ClearExecutionLine();
        _workspace.Status = new StatusMessage(L.T("Debugging ended"));
    }

    private void RemoveReadyFile()
    {
        if (_readyFile is not null)
        {
            try
            {
                File.Delete(_readyFile);
            }
            catch (IOException)
            {
                // Already gone.
            }
        }

        _readyFile = null;
    }

    private void ShowExecutionLine(StackFrame frame)
    {
        ClearExecutionLine();
        if (frame.FilePath is not { } path || !File.Exists(path))
        {
            return;
        }

        if (_workspace.OpenFile(path, frame.Line, Math.Max(1, frame.Column)) is { } document)
        {
            document.ExecutionLine = frame.Line;
        }
    }

    private void ClearExecutionLine()
    {
        foreach (var document in _workspace.OpenDocuments.Where(d => d.ExecutionLine is not null))
        {
            document.ExecutionLine = null;
        }
    }

    private void AppendConsole(string text, bool isError = false)
    {
        _consoleId++;
        var line = new OutputLog.Line(_consoleId, OutputLog.Channel.Debugger, text, isError);
        Console = Console.Count >= 5000 ? [.. Console.Skip(Console.Count - 4999), line] : [.. Console, line];
    }

    private static async Task<T?> Try<T>(Func<Task<T>> work)
        where T : class
    {
        try
        {
            return await work();
        }
        catch (DebugException)
        {
            return null;
        }
    }

    partial void OnSelectedFrameIdChanged(int? oldValue, int? newValue)
    {
        if (oldValue != newValue && IsStopped)
        {
            _ = LoadFrame();
        }
    }
}
