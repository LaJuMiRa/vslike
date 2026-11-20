using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.App.Editor;
using VsLike.App.Shell;
using VsLike.Core.Code;
using VsLike.Core.Files;
using VsLike.Core.Localization;
using VsLike.Debugging;
using VsLike.Debugging.Netcoredbg;
using VsLike.Editor.Debugging;
using VsLike.Projects.Build;
using VsLike.Projects.Cli;

namespace VsLike.App.Debugging;

/// <summary>
/// Debugger der IDE (netcoredbg via DAP): Breakpoints (auch bedingt), Start mit Build und Startprofil, Halt mit
/// Aufrufliste, Threads, Variablen, Überwachung, Debug-Konsole und Hover-Werten; Schritte über/hinein/heraus.
/// </summary>
public sealed partial class DebugController : ObservableObject, IDisposable
{
    private readonly Workbench _workbench;
    private readonly Dictionary<string, List<EditorBreakpoint>> _breakpoints = new(StringComparer.Ordinal);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private DebugSession? _session;
    private EditorDocumentViewModel? _executionDocument;
    private bool _stopping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanStep))]
    private bool _isDebugging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStep))]
    private bool _isStopped;

    [ObservableProperty]
    private StackFrameInfo? _selectedFrame;

    [ObservableProperty]
    private DebugThread? _selectedThread;

    public DebugController(Workbench workbench)
    {
        _workbench = workbench;
        workbench.Documents.DocumentOpened += (_, document) => Track(document);
        foreach (var document in workbench.Documents.OpenDocuments)
        {
            Track(document);
        }
    }

    public ObservableCollection<VariableNodeViewModel> Locals { get; } = [];

    public ObservableCollection<WatchItem> Watches { get; } = [];

    public ObservableCollection<StackFrameInfo> Frames { get; } = [];

    public ObservableCollection<DebugThread> Threads { get; } = [];

    public ObservableCollection<string> Console { get; } = [];

    public bool CanStart => !IsDebugging || IsStopped;

    public bool CanStep => IsDebugging && IsStopped;

    public IReadOnlyList<EditorBreakpoint> BreakpointsOf(string path)
        => _breakpoints.TryGetValue(RealPath.Resolve(path), out var list) ? list : [];

    public void ToggleBreakpoint(string path, int line)
    {
        var list = List(path);
        if (list.RemoveAll(b => b.Line == line) == 0)
        {
            list.Add(new EditorBreakpoint(line));
        }

        _ = PushBreakpointsAsync(path);
    }

    public void SetCondition(string path, int line, string? condition)
    {
        var list = List(path);
        list.RemoveAll(b => b.Line == line);
        list.Add(new EditorBreakpoint(line, string.IsNullOrWhiteSpace(condition) ? null : condition.Trim()));
        _ = PushBreakpointsAsync(path);
    }

    /// <summary>F5: continues when stopped, otherwise builds the startup project and starts it under the debugger.</summary>
    public async Task StartOrContinueAsync()
    {
        if (_session is not null)
        {
            if (IsStopped)
            {
                await StepAsync((s, t) => s.ContinueAsync(t, CancellationToken.None)).ConfigureAwait(true);
            }

            return;
        }

        var build = _workbench.Build;
        if (build.StartupProject is not { } startup)
        {
            _workbench.Shell.StatusText = Loc.Get("Run.NoStartupProject");
            return;
        }

        if (await build.BuildAsync(BuildKind.Build, startup.Path).ConfigureAwait(true) is not { Succeeded: true })
        {
            return;
        }

        try
        {
            var target = await TargetPathAsync(startup.Path, build.Configuration).ConfigureAwait(true);
            _workbench.Shell.StatusText = Loc.Get("Debug.Preparing");
            var netcoredbg = await NetcoredbgPackage.EnsureAsync(_http, CancellationToken.None).ConfigureAwait(true);
            var profile = build.LaunchProfile?.Profile;
            var launch = new LaunchRequest(
                target,
                SplitArguments(profile?.CommandLineArgs),
                profile?.WorkingDirectory is { Length: > 0 } directory ? Path.GetFullPath(directory.Replace("$(ProjectDir)", Path.GetDirectoryName(startup.Path) + "/", StringComparison.OrdinalIgnoreCase), Path.GetDirectoryName(startup.Path)!) : Path.GetDirectoryName(startup.Path)!,
                profile?.EnvironmentVariables ?? new Dictionary<string, string>());
            Console.Clear();
            var breakpoints = _breakpoints.ToDictionary(b => b.Key, b => (IReadOnlyList<SourceBreakpoint>)[.. b.Value.Select(x => new SourceBreakpoint(x.Line, x.Condition))], StringComparer.Ordinal);
            var dotnet = DotnetCli.LocateDotnet();
            var (session, states) = await DebugSession.StartAsync(netcoredbg, launch, breakpoints, Path.IsPathRooted(dotnet) ? Path.GetDirectoryName(dotnet) : null, Attach, CancellationToken.None).ConfigureAwait(true);
            _session = session;
            IsDebugging = true;
            ApplyStates(states);
            _workbench.Shell.StatusText = Loc.Format("Debug.Running", startup.Name);
            _workbench.Shell.ShowPanel(ShellPanels.DebugConsole.Id);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or HttpRequestException or VsLike.Debugging.Dap.DapException or TimeoutException or PlatformNotSupportedException or TaskCanceledException)
        {
            _workbench.Shell.StatusText = Loc.Format("Debug.Failed", ex.Message);
            await EndAsync().ConfigureAwait(true);
        }
    }

    public Task StepOverAsync() => StepAsync((s, t) => s.NextAsync(t, CancellationToken.None));

    public Task StepIntoAsync() => StepAsync((s, t) => s.StepInAsync(t, CancellationToken.None));

    public Task StepOutAsync() => StepAsync((s, t) => s.StepOutAsync(t, CancellationToken.None));

    public async Task PauseAsync()
    {
        if (_session is { } session && !IsStopped)
        {
            var thread = SelectedThread?.Id ?? (await session.ThreadsAsync(CancellationToken.None).ConfigureAwait(true)).FirstOrDefault()?.Id ?? 0;
            await session.PauseAsync(thread, CancellationToken.None).ConfigureAwait(true);
        }
    }

    public async Task StopAsync()
    {
        if (_session is { } session)
        {
            await session.StopAsync().ConfigureAwait(true);
            await EndAsync().ConfigureAwait(true);
        }
    }

    public async Task SelectFrameAsync(StackFrameInfo frame)
    {
        SelectedFrame = frame;
        await ShowFrameAsync(frame).ConfigureAwait(true);
        await LoadLocalsAsync(frame).ConfigureAwait(true);
        await RefreshWatchesAsync().ConfigureAwait(true);
    }

    public async Task SelectThreadAsync(DebugThread thread)
    {
        if (_session is not { } session || !(IsStopped || _stopping))
        {
            return;
        }

        SelectedThread = thread;
        await LoadFramesAsync(session, thread.Id).ConfigureAwait(true);
    }

    public async Task AddWatchAsync(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        var item = new WatchItem(expression.Trim());
        Watches.Add(item);
        await EvaluateWatchAsync(item).ConfigureAwait(true);
    }

    public void RemoveWatch(WatchItem item) => Watches.Remove(item);

    /// <summary>Evaluates an expression typed into the debug console.</summary>
    public async Task EvaluateInConsoleAsync(string expression)
    {
        if (_session is not { } session || string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        Console.Add("> " + expression);
        try
        {
            Console.Add((await session.EvaluateAsync(expression, SelectedFrame?.Id, "repl", CancellationToken.None).ConfigureAwait(true)).Value);
        }
        catch (VsLike.Debugging.Dap.DapException ex)
        {
            Console.Add(ex.Message);
        }
    }

    /// <summary>Hover value while stopped: evaluates the (dotted) expression under the mouse.</summary>
    public async Task<QuickInfo?> HoverAsync(EditorDocumentViewModel document, int offset)
    {
        if (_session is not { } session || !IsStopped || SelectedFrame is null)
        {
            return null;
        }

        var text = document.TextDocument.Text;
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.'))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        var expression = text[start..end].Trim('.');
        if (expression.Length == 0 || char.IsDigit(expression[0]))
        {
            return null;
        }

        try
        {
            var result = await session.EvaluateAsync(expression, SelectedFrame.Id, "hover", CancellationToken.None).ConfigureAwait(true);
            return new QuickInfo(start, end - start, $"{expression} = {result.Value}", result.Type, []);
        }
        catch (VsLike.Debugging.Dap.DapException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _ = _session?.StopAsync();
        _http.Dispose();
    }

    internal static IReadOnlyList<string> SplitArguments(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return result;
        }

        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private List<EditorBreakpoint> List(string path)
    {
        var key = RealPath.Resolve(path);
        if (!_breakpoints.TryGetValue(key, out var list))
        {
            list = [];
            _breakpoints[key] = list;
        }

        return list;
    }

    private void Track(EditorDocumentViewModel document)
    {
        void Hook()
        {
            if (document.Session is not { } editor || document.FilePath is not { } path)
            {
                return;
            }

            editor.BreakpointMargin.SetBreakpoints(BreakpointsOf(path));
            editor.BreakpointMargin.ToggleRequested += (_, line) => ToggleBreakpoint(path, line);
            editor.BreakpointMargin.EditRequested += async (_, line) =>
            {
                var current = BreakpointsOf(path).FirstOrDefault(b => b.Line == line)?.Condition ?? string.Empty;
                if (await _workbench.Dialogs.PromptAsync(Loc.Get("Debug.Condition"), Loc.Get("Debug.ConditionPrompt"), current).ConfigureAwait(true) is { } condition)
                {
                    SetCondition(path, line, condition);
                }
            };
            editor.BreakpointMargin.Moved += (_, moved) =>
            {
                var list = List(path);
                list.Clear();
                list.AddRange(moved);
            };
            if (_executionDocument == document && SelectedFrame is { } frame)
            {
                editor.SetExecutionLine(frame.Line);
            }
        }

        document.SessionChanged += (_, _) => Hook();
        Hook();
    }

    private async Task PushBreakpointsAsync(string path)
    {
        var key = RealPath.Resolve(path);
        var list = List(path);
        foreach (var document in _workbench.Documents.OpenDocuments.Where(d => d.FilePath is { } p && RealPath.Resolve(p) == key))
        {
            document.Session?.BreakpointMargin.SetBreakpoints(list);
        }

        if (_session is { } session)
        {
            try
            {
                var states = await session.SetBreakpointsAsync(key, [.. list.Select(b => new SourceBreakpoint(b.Line, b.Condition))], CancellationToken.None).ConfigureAwait(true);
                ApplyStates(new Dictionary<string, IReadOnlyList<BreakpointState>> { [key] = states });
            }
            catch (VsLike.Debugging.Dap.DapException ex)
            {
                _workbench.Shell.StatusText = ex.Message;
            }
        }
    }

    private void ApplyStates(IReadOnlyDictionary<string, IReadOnlyList<BreakpointState>> states)
    {
        foreach (var (file, list) in states)
        {
            if (_breakpoints.TryGetValue(file, out var breakpoints))
            {
                for (var i = 0; i < breakpoints.Count && i < list.Count; i++)
                {
                    breakpoints[i] = breakpoints[i] with { Verified = true };
                }
            }
        }
    }

    private void Attach(DebugSession session)
    {
        session.Stopped += (_, stop) => Dispatcher.UIThread.Post(async () => await OnStoppedAsync(stop).ConfigureAwait(true));
        session.Continued += (_, _) => Dispatcher.UIThread.Post(OnContinued);
        session.Output += (_, output) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var line in output.Text.TrimEnd('\n').Split('\n'))
            {
                Console.Add(line.TrimEnd('\r'));
            }
        });
        session.Exited += (_, code) => Dispatcher.UIThread.Post(() => _workbench.Shell.StatusText = Loc.Format("Debug.Exited", code));
        session.Terminated += (_, _) => Dispatcher.UIThread.Post(async () => await EndAsync().ConfigureAwait(true));
    }

    private async Task OnStoppedAsync(StoppedInfo stop)
    {
        if (_session is not { } session)
        {
            return;
        }

        _workbench.Shell.StatusText = Loc.Format("Debug.Stopped", stop.Description ?? stop.Reason);
        Threads.Clear();
        foreach (var thread in await session.ThreadsAsync(CancellationToken.None).ConfigureAwait(true))
        {
            Threads.Add(thread);
        }

        SelectedThread = Threads.FirstOrDefault(t => t.Id == stop.ThreadId) ?? Threads.FirstOrDefault();

        // Stopped only counts once frames, variables and watches are loaded (buttons and tests rely on it).
        _stopping = true;
        await LoadFramesAsync(session, stop.ThreadId).ConfigureAwait(true);
        _stopping = false;
        IsStopped = true;
        await RefreshWatchesAsync().ConfigureAwait(true);
        _workbench.Shell.ShowPanel(ShellPanels.Debug.Id);
    }

    private async Task LoadFramesAsync(DebugSession session, int threadId)
    {
        Frames.Clear();
        foreach (var frame in await session.StackTraceAsync(threadId, CancellationToken.None).ConfigureAwait(true))
        {
            Frames.Add(frame);
        }

        if ((Frames.FirstOrDefault(f => f.FilePath is not null) ?? Frames.FirstOrDefault()) is { } top)
        {
            await SelectFrameAsync(top).ConfigureAwait(true);
        }
    }

    private async Task ShowFrameAsync(StackFrameInfo frame)
    {
        ClearExecutionLine();
        if (frame.FilePath is not { } path || !File.Exists(path))
        {
            return;
        }

        await _workbench.Documents.OpenAsync(path, frame.Line, Math.Max(1, frame.Column)).ConfigureAwait(true);
        _executionDocument = _workbench.Documents.Active;
        _executionDocument?.Session?.SetExecutionLine(frame.Line);
    }

    private async Task LoadLocalsAsync(StackFrameInfo frame)
    {
        Locals.Clear();
        if (_session is not { } session)
        {
            return;
        }

        foreach (var scope in await session.ScopesAsync(frame.Id, CancellationToken.None).ConfigureAwait(true))
        {
            var node = new VariableNodeViewModel(scope.Name, string.Empty, null, scope.VariablesReference, LoadVariablesAsync);
            Locals.Add(node);
            await node.LoadChildrenAsync().ConfigureAwait(true);
            node.IsExpanded = true;
        }
    }

    private async Task<IReadOnlyList<VariableNodeViewModel>> LoadVariablesAsync(int reference)
    {
        if (_session is not { } session)
        {
            return [];
        }

        var variables = await session.VariablesAsync(reference, CancellationToken.None).ConfigureAwait(true);
        return [.. variables.Select(v => new VariableNodeViewModel(v.Name, v.Value, v.Type, v.VariablesReference, LoadVariablesAsync))];
    }

    private async Task RefreshWatchesAsync()
    {
        foreach (var watch in Watches)
        {
            await EvaluateWatchAsync(watch).ConfigureAwait(true);
        }
    }

    private async Task EvaluateWatchAsync(WatchItem item)
    {
        if (_session is not { } session || !(IsStopped || _stopping))
        {
            item.Value = string.Empty;
            return;
        }

        try
        {
            item.Value = (await session.EvaluateAsync(item.Expression, SelectedFrame?.Id, "watch", CancellationToken.None).ConfigureAwait(true)).Value;
        }
        catch (VsLike.Debugging.Dap.DapException ex)
        {
            item.Value = ex.Message;
        }
    }

    private async Task StepAsync(Func<DebugSession, int, Task> step)
    {
        if (_session is { } session && IsStopped && SelectedThread is { } thread)
        {
            OnContinued();
            try
            {
                await step(session, thread.Id).ConfigureAwait(true);
            }
            catch (VsLike.Debugging.Dap.DapException ex)
            {
                _workbench.Shell.StatusText = ex.Message;
            }
        }
    }

    private void OnContinued()
    {
        IsStopped = false;
        ClearExecutionLine();
    }

    private void ClearExecutionLine()
    {
        _executionDocument?.Session?.SetExecutionLine(null);
        _executionDocument = null;
    }

    private async Task EndAsync()
    {
        var session = _session;
        _session = null;
        IsDebugging = false;
        IsStopped = false;
        ClearExecutionLine();
        Frames.Clear();
        Threads.Clear();
        Locals.Clear();
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async Task<string> TargetPathAsync(string project, string configuration)
    {
        var result = await _workbench.Cli.RunAsync(["msbuild", project, "-getProperty:TargetPath", "-p:Configuration=" + configuration, "-nologo"], Path.GetDirectoryName(project), null, CancellationToken.None).ConfigureAwait(true);
        var path = result.Output.Where(l => !l.IsError).Select(l => l.Text.Trim()).LastOrDefault(l => l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        return path is not null && File.Exists(path) ? RealPath.Resolve(path) : throw new InvalidOperationException(Loc.Format("Debug.NoTarget", Path.GetFileName(project)));
    }
}
