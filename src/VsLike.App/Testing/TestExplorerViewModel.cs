using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.App.Output;
using VsLike.App.Shell;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Projects.Testing;

namespace VsLike.App.Testing;

/// <summary>
/// Test-Explorer: findet Tests aller Testprojekte der Solution, führt alle, ausgewählte oder fehlgeschlagene Tests aus
/// und zeigt Ergebnis, Dauer, Meldung und Stacktrace. Doppelklick springt zum Test bzw. zur fehlschlagenden Zeile.
/// </summary>
public sealed partial class TestExplorerViewModel : ObservableObject
{
    private readonly TestService _service;
    private readonly OutputViewModel _output;
    private readonly ShellViewModel _shell;
    private readonly ILanguageFeatures _features;
    private readonly IDocumentNavigator _navigator;
    private readonly Func<Task> _saveAll;
    private readonly Func<string> _configuration;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDetails))]
    private TestNodeViewModel? _selected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = string.Empty;

    public TestExplorerViewModel(TestService service, OutputViewModel output, ShellViewModel shell, ILanguageFeatures features, IDocumentNavigator navigator, Func<Task> saveAll, Func<string> configuration)
    {
        _service = service;
        _output = output;
        _shell = shell;
        _features = features;
        _navigator = navigator;
        _saveAll = saveAll;
        _configuration = configuration;
    }

    public ObservableCollection<TestNodeViewModel> Roots { get; } = [];

    public bool HasTestProjects => Roots.Count > 0;

    public string SelectedDetails => Selected is { } node
        ? string.Join("\n\n", new[] { node.Message, node.StackTrace }.Where(t => !string.IsNullOrWhiteSpace(t)))
        : string.Empty;

    public IEnumerable<TestNodeViewModel> AllLeaves => Roots.SelectMany(r => r.Leaves());

    /// <summary>Shows the test projects among the given projects (keeps known results of unchanged projects).</summary>
    public void SetProjects(IEnumerable<string> projectPaths)
    {
        var tests = projectPaths.Where(TestService.IsTestProject).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var stale in Roots.Where(r => !tests.Contains(r.ProjectPath)).ToList())
        {
            Roots.Remove(stale);
        }

        for (var i = 0; i < tests.Count; i++)
        {
            if (Roots.FirstOrDefault(r => r.ProjectPath == tests[i]) is null)
            {
                Roots.Insert(Math.Min(i, Roots.Count), new TestNodeViewModel(TestNodeKind.Project, Path.GetFileNameWithoutExtension(tests[i]), tests[i], tests[i], null));
            }
        }

        OnPropertyChanged(nameof(HasTestProjects));
        UpdateSummary();
    }

    public Task DiscoverAsync() => RunExclusiveAsync(Loc.Get("Tests.Discovering"), async token =>
    {
        foreach (var project in Roots.ToList())
        {
            var (names, process) = await Task.Run(() => _service.DiscoverAsync(project.ProjectPath, _configuration(), _output.Append, token), CancellationToken.None).ConfigureAwait(true);
            if (process.WasCancelled)
            {
                return;
            }

            project.Children.Clear();
            foreach (var name in names)
            {
                AddTest(project, name);
            }

            project.Aggregate();
        }
    });

    /// <summary>Runs all tests (node null) or the tests below a node.</summary>
    public Task RunAsync(TestNodeViewModel? node = null)
    {
        var projects = node is null ? Roots.ToList() : [Root(node)];
        return RunExclusiveAsync(Loc.Get("Tests.Running"), async token =>
        {
            foreach (var project in projects)
            {
                var scope = node is null || node.Kind == TestNodeKind.Project ? project : node;
                var filter = scope.Kind switch
                {
                    TestNodeKind.Project => null,
                    TestNodeKind.Namespace or TestNodeKind.Class => TestService.BuildFilter([], [scope.FullName + "."]),
                    TestNodeKind.Case => TestService.BuildFilter([scope.Parent!.FullName], []),
                    _ => TestService.BuildFilter([scope.FullName], []),
                };
                var affected = (scope.Kind == TestNodeKind.Case ? scope.Parent! : scope).Leaves().ToList();
                if (!await RunProjectAsync(project, filter, affected, token).ConfigureAwait(true))
                {
                    return;
                }
            }
        });
    }

    public Task RunFailedAsync()
    {
        var failed = AllLeaves.Where(l => l.Outcome == TestOutcome.Failed).GroupBy(Root).ToList();
        return RunExclusiveAsync(Loc.Get("Tests.Running"), async token =>
        {
            foreach (var group in failed)
            {
                var names = group.Select(l => l.Kind == TestNodeKind.Case ? l.Parent!.FullName : l.FullName).Distinct(StringComparer.Ordinal).ToList();
                var affected = group.SelectMany(l => l.Kind == TestNodeKind.Case ? l.Parent!.Leaves() : [l]).Distinct().ToList();
                if (!await RunProjectAsync(group.Key, TestService.BuildFilter(names, []), affected, token).ConfigureAwait(true))
                {
                    return;
                }
            }
        });
    }

    public void Cancel() => _cancellation?.Cancel();

    /// <summary>Opens the failing line of a failed test, otherwise the test's declaration.</summary>
    public async Task OpenAsync(TestNodeViewModel node)
    {
        if (TestOutputParser.FirstSourceFrame(node.StackTrace) is { } frame && File.Exists(frame.File))
        {
            await _navigator.OpenAsync(frame.File, frame.Line).ConfigureAwait(true);
            return;
        }

        var (type, member) = node.Kind switch
        {
            TestNodeKind.Class => (node.FullName, (string?)null),
            TestNodeKind.Method => (node.Parent!.FullName, node.Name),
            TestNodeKind.Case => (node.Parent!.Parent!.FullName, node.Parent.Name),
            _ => (null, null),
        };
        if (type is not null && await _features.FindDeclarationAsync(type, member, CancellationToken.None).ConfigureAwait(true) is { } location)
        {
            await _navigator.OpenAsync(location.FilePath, location.Line, location.Column).ConfigureAwait(true);
        }
    }

    internal static TestNodeViewModel AddTest(TestNodeViewModel project, string fullName)
    {
        var name = TestName.Parse(fullName);
        var parent = project;
        if (name.Namespace.Length > 0)
        {
            parent = parent.GetOrAdd(TestNodeKind.Namespace, name.Namespace, name.Namespace);
        }

        if (name.ClassName.Length > 0)
        {
            parent = parent.GetOrAdd(TestNodeKind.Class, name.ClassName, name.ClassFullName);
        }

        var method = parent.GetOrAdd(TestNodeKind.Method, name.Method, name.MethodFullName);
        return name.Arguments.Length == 0 ? method : method.GetOrAdd(TestNodeKind.Case, name.Method + name.Arguments, fullName);
    }

    private static TestNodeViewModel Root(TestNodeViewModel node)
    {
        while (node.Parent is not null)
        {
            node = node.Parent;
        }

        return node;
    }

    private static TestNodeViewModel? FindLeaf(TestNodeViewModel project, string name)
        => project.SelfAndDescendants().FirstOrDefault(n => n.IsLeaf && n.FullName == name);

    private async Task<bool> RunProjectAsync(TestNodeViewModel project, string? filter, IReadOnlyList<TestNodeViewModel> affected, CancellationToken token)
    {
        foreach (var leaf in affected)
        {
            leaf.Outcome = TestOutcome.Running;
        }

        project.Aggregate();
        var (results, process) = await Task.Run(() => _service.RunAsync(project.ProjectPath, _configuration(), filter, _output.Append, token), CancellationToken.None).ConfigureAwait(true);
        var reported = new HashSet<TestNodeViewModel>();
        foreach (var result in results)
        {
            var leaf = FindLeaf(project, result.Name) ?? AddTest(project, result.Name);
            leaf.Outcome = result.Outcome;
            leaf.Duration = result.Duration;
            leaf.Message = result.Message;
            leaf.StackTrace = result.StackTrace;
            reported.Add(leaf);
        }

        foreach (var leaf in affected.Where(l => !reported.Contains(l)))
        {
            leaf.Outcome = TestOutcome.NotRun;
        }

        project.Aggregate();
        if (results.Count == 0 && !process.Succeeded && !process.WasCancelled)
        {
            _shell.ShowPanel(ShellPanels.Output.Id);
        }

        return !process.WasCancelled;
    }

    private async Task RunExclusiveAsync(string title, Func<CancellationToken, Task> work)
    {
        if (IsBusy || Roots.Count == 0)
        {
            return;
        }

        await _saveAll().ConfigureAwait(true);
        using var cancellation = _cancellation = new CancellationTokenSource();
        IsBusy = true;
        _output.Begin(title);
        _shell.StatusText = title;
        try
        {
            await work(cancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
            foreach (var running in AllLeaves.Where(l => l.Outcome == TestOutcome.Running))
            {
                running.Outcome = TestOutcome.NotRun;
            }

            foreach (var root in Roots)
            {
                root.Aggregate();
            }

            _output.Flush();
            UpdateSummary();
            _shell.StatusText = Summary;
            OnPropertyChanged(nameof(SelectedDetails));
        }
    }

    private void UpdateSummary()
    {
        var leaves = AllLeaves.ToList();
        var passed = leaves.Count(l => l.Outcome == TestOutcome.Passed);
        var failed = leaves.Count(l => l.Outcome == TestOutcome.Failed);
        var skipped = leaves.Count(l => l.Outcome == TestOutcome.Skipped);
        Summary = leaves.Count == 0 ? string.Empty : Loc.Format("Tests.Summary", passed, failed, skipped, leaves.Count);
    }
}
