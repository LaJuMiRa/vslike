using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>A node of the test tree: project › namespace › class › test (› data-driven case).</summary>
/// <param name="FullName">Full test name for tests and cases, the prefix (<c>Ns.Class.</c>) for groups.</param>
public sealed record TestNode(string Id, string Name, TestNode.NodeKind Kind, string Project, string FullName, TestOutcome Outcome, IReadOnlyList<TestNode>? Children = null)
{
    public enum NodeKind
    {
        Project,
        Namespace,
        TestClass,
        Test,
        TestCase,
    }
}

public sealed partial class TestController : ObservableObject
{
    private readonly Workspace _workspace;
    private CancellationTokenSource? _task;

    public TestController(Workspace workspace)
    {
        _workspace = workspace;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tree))]
    public partial IReadOnlyList<SolutionProject> TestProjects { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tree), nameof(TotalCount))]
    public partial IReadOnlyDictionary<string, IReadOnlyList<string>> Tests { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tree), nameof(PassedCount), nameof(FailedCount))]
    public partial IReadOnlyDictionary<string, TestResult> Results { get; private set; } = new Dictionary<string, TestResult>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tree))]
    public partial IReadOnlySet<string> Running { get; private set; } = new HashSet<string>();

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? SelectedId { get; set; }

    public int PassedCount => Results.Values.Count(r => r.Outcome == TestOutcome.Passed);

    public int FailedCount => Results.Values.Count(r => r.Outcome == TestOutcome.Failed);

    public int TotalCount => Tests.Values.Sum(t => t.Count);

    public IReadOnlyList<TestNode> Tree => [.. TestProjects.Select(ProjectNode)];

    public void ReloadProjects()
    {
        TestProjects = [.. (_workspace.Solution?.Projects ?? []).Where(IsTestProject)];
        Tests = Tests.Where(t => TestProjects.Any(p => p.FilePath == t.Key)).ToDictionary(t => t.Key, t => t.Value);
    }

    public TestResult? ResultFor(TestNode node) => Results.GetValueOrDefault(node.FullName);

    public void Discover()
    {
        if (IsBusy)
        {
            return;
        }

        _task = new CancellationTokenSource();
        _ = Busy(DiscoverAll);
    }

    /// <summary>Runs all tests (<paramref name="nodes"/> empty), a selection, or the failed ones.</summary>
    public void Run(IReadOnlyList<TestNode>? nodes = null, bool onlyFailed = false)
    {
        if (IsBusy)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _task = cancellation;
        _ = Busy(() => RunAll(nodes ?? [], onlyFailed, cancellation.Token));
    }

    public void Cancel()
    {
        _task?.Cancel();
    }

    /// <summary>Opens the test's source: the failing frame if there is one, otherwise its declaration.</summary>
    public async Task Open(TestNode node)
    {
        if (TestService.FirstSourceFrame(Results.GetValueOrDefault(node.FullName)?.StackTrace) is { } frame)
        {
            _workspace.OpenFile(frame.File, frame.Line);
            return;
        }

        var baseName = node.FullName.TrimEnd('.', '(');
        var name = new TestName(baseName);
        var isTest = node.Kind is TestNode.NodeKind.Test or TestNode.NodeKind.TestCase;
        var typeName = isTest ? name.ClassFullName : baseName;
        var member = isTest ? name.Method : null;
        if (_workspace.Engine is { } engine)
        {
            try
            {
                if (await engine.Declaration(typeName, member) is { } location)
                {
                    _workspace.Navigate(location);
                }
            }
            catch (EngineException)
            {
                // try? in the original.
            }
        }
    }

    private static bool IsTestProject(SolutionProject project)
    {
        try
        {
            return ProjectFile.Load(project.FilePath).IsTestProject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task Busy(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        finally
        {
            IsBusy = false;
            Running = new HashSet<string>();
        }
    }

    private async Task DiscoverAll()
    {
        CommandRunner runner;
        try
        {
            runner = CommandRunner.Dotnet(_workspace.Preferences.EffectiveLanguage);
        }
        catch (CommandException)
        {
            return;
        }

        var service = new TestService(runner);
        _workspace.Status = new StatusMessage(L.T("Discovering tests…"), StatusStyle.Progress);
        foreach (var project in TestProjects)
        {
            try
            {
                var (tests, process) = await service.Discover(project.FilePath, _workspace.Build.Configuration, _workspace.Output.Sink(OutputLog.Channel.Tests));
                Tests = new Dictionary<string, IReadOnlyList<string>>(Tests) { [project.FilePath] = tests };
                if (tests.Count == 0 && !process.Succeeded)
                {
                    _workspace.Diagnostics.SetBuild(BuildOutputParser.Parse(process.Output));
                }
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                // try? in the original.
            }
        }

        _workspace.Status = new StatusMessage(L.T("%lld tests found", TotalCount));
    }

    private async Task RunAll(IReadOnlyList<TestNode> nodes, bool onlyFailed, CancellationToken token)
    {
        if (Tests.Count == 0)
        {
            await DiscoverAll();
        }

        CommandRunner runner;
        try
        {
            runner = CommandRunner.Dotnet(_workspace.Preferences.EffectiveLanguage);
        }
        catch (CommandException)
        {
            return;
        }

        _workspace.SaveAll();
        _workspace.Output.Clear(OutputLog.Channel.Tests);
        var service = new TestService(runner);
        foreach (var project in TestProjects)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            await RunProject(service, project, nodes, onlyFailed, token);
        }

        var failed = FailedCount;
        _workspace.Status = failed == 0
            ? new StatusMessage(L.T("%lld tests passed", PassedCount), StatusStyle.Success)
            : new StatusMessage(L.T("%lld tests failed", failed), StatusStyle.Error);
    }

    private async Task RunProject(TestService service, SolutionProject project, IReadOnlyList<TestNode> nodes, bool onlyFailed, CancellationToken token)
    {
        var all = Tests.GetValueOrDefault(project.FilePath) ?? [];
        List<string> exact = [];
        List<string> prefixes = [];
        if (onlyFailed)
        {
            exact = [.. all.Where(t => Results.GetValueOrDefault(t)?.Outcome == TestOutcome.Failed)];
            if (exact.Count == 0)
            {
                return;
            }
        }
        else if (nodes.Count > 0)
        {
            var mine = nodes.Where(n => n.Project == project.FilePath).ToList();
            if (mine.Count == 0)
            {
                return;
            }

            if (!mine.Any(n => n.Kind == TestNode.NodeKind.Project))
            {
                exact = [.. mine.Where(n => n.Kind == TestNode.NodeKind.TestCase || (n.Kind == TestNode.NodeKind.Test && n.Children is null)).Select(n => n.FullName)];
                prefixes = [.. mine.Where(n => n.Kind is TestNode.NodeKind.Namespace or TestNode.NodeKind.TestClass || (n.Kind == TestNode.NodeKind.Test && n.Children is not null)).Select(n => n.FullName)];
            }
        }

        var filter = exact.Count == 0 && prefixes.Count == 0 ? null : TestService.Filter(exact, prefixes);
        Running = all.Where(n => filter is null || exact.Contains(n) || prefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal))).ToHashSet();
        _workspace.Status = new StatusMessage(L.T("Running tests in %@…", project.Name), StatusStyle.Progress);
        try
        {
            var (results, process) = await service.Run(project.FilePath, _workspace.Build.Configuration, filter, _workspace.Output.Sink(OutputLog.Channel.Tests), token);
            var updated = new Dictionary<string, TestResult>(Results);
            foreach (var result in results)
            {
                updated[result.FullName] = result;
            }

            Results = updated;
            if (results.Count == 0 && !process.Succeeded)
            {
                _workspace.Diagnostics.SetBuild(BuildOutputParser.Parse(process.Output));
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            // try? in the original.
        }
    }

    private TestNode ProjectNode(SolutionProject project)
    {
        var names = Tests.GetValueOrDefault(project.FilePath) ?? [];
        var namespaces = names.GroupBy(n => new TestName(n).Namespace).OrderBy(g => g.Key, StringComparer.Ordinal);
        var children = namespaces.Select(ns =>
        {
            var classes = ns.GroupBy(n => new TestName(n).ClassName).OrderBy(g => g.Key, StringComparer.Ordinal).Select(c => ClassNode(project, ns.Key, c.Key, [.. c])).ToList();
            return new TestNode(project.FilePath + "|ns:" + ns.Key, ns.Key.Length == 0 ? "–" : ns.Key, TestNode.NodeKind.Namespace, project.FilePath, ns.Key + ".", OutcomeOf([.. ns]), classes);
        }).ToList();
        return new TestNode(project.FilePath, project.Name, TestNode.NodeKind.Project, project.FilePath, string.Empty, OutcomeOf(names), children);
    }

    private TestNode ClassNode(SolutionProject project, string ns, string className, IReadOnlyList<string> names)
    {
        var classFull = ns.Length == 0 ? className : ns + "." + className;
        var tests = names.GroupBy(n => new TestName(n).MethodFullName).OrderBy(g => g.Key, StringComparer.Ordinal).Select(m =>
        {
            var method = m.Key;
            var cases = m.ToList();
            if (cases.Count == 1 && cases[0] == method)
            {
                return new TestNode(project.FilePath + "|" + method, new TestName(method).Method, TestNode.NodeKind.Test, project.FilePath, method, OutcomeOf([method]));
            }

            var caseNodes = cases.Order(StringComparer.Ordinal).Select(name => new TestNode(
                project.FilePath + "|" + name,
                new TestName(name).Method + new TestName(name).Arguments,
                TestNode.NodeKind.TestCase,
                project.FilePath,
                name,
                OutcomeOf([name]))).ToList();
            return new TestNode(project.FilePath + "|" + method, new TestName(method).Method, TestNode.NodeKind.Test, project.FilePath, method + "(", OutcomeOf(cases), caseNodes);
        }).ToList();
        return new TestNode(project.FilePath + "|" + classFull, className, TestNode.NodeKind.TestClass, project.FilePath, classFull + ".", OutcomeOf(names), tests);
    }

    private TestOutcome OutcomeOf(IReadOnlyList<string> names)
    {
        if (names.Any(Running.Contains))
        {
            return TestOutcome.Running;
        }

        var outcomes = names.Select(n => Results.GetValueOrDefault(n)?.Outcome).OfType<TestOutcome>().ToList();
        if (outcomes.Contains(TestOutcome.Failed))
        {
            return TestOutcome.Failed;
        }

        if (outcomes.Count == 0 || outcomes.Count < names.Count)
        {
            return TestOutcome.NotRun;
        }

        return outcomes.All(o => o == TestOutcome.Skipped) ? TestOutcome.Skipped : TestOutcome.Passed;
    }
}
