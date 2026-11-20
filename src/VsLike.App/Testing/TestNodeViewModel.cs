using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Projects.Testing;

namespace VsLike.App.Testing;

/// <summary>Knoten im Test-Explorer: Projekt → Namespace → Klasse → Methode → (Datenfall).</summary>
public sealed partial class TestNodeViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(OutcomeClass))]
    private TestOutcome _outcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private TimeSpan? _duration;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _stackTrace;

    [ObservableProperty]
    private bool _isExpanded;

    public TestNodeViewModel(TestNodeKind kind, string name, string fullName, string projectPath, TestNodeViewModel? parent)
    {
        Kind = kind;
        Name = name;
        FullName = fullName;
        ProjectPath = projectPath;
        Parent = parent;
        IsExpanded = kind is TestNodeKind.Project or TestNodeKind.Namespace;
    }

    public TestNodeKind Kind { get; }

    public string Name { get; }

    /// <summary>Gets the name used for filters and result matching (project path for project nodes).</summary>
    public string FullName { get; }

    public string ProjectPath { get; }

    public TestNodeViewModel? Parent { get; }

    public ObservableCollection<TestNodeViewModel> Children { get; } = [];

    public bool IsLeaf => Kind is TestNodeKind.Case || (Kind is TestNodeKind.Method && Children.Count == 0);

    public string Glyph => Outcome switch
    {
        TestOutcome.Passed => "✓",
        TestOutcome.Failed => "✕",
        TestOutcome.Skipped => "–",
        TestOutcome.Running => "…",
        _ => "○",
    };

    public string OutcomeClass => Outcome.ToString().ToLowerInvariant();

    public string DurationText => Duration is not { } d || !IsLeaf ? string.Empty : FormatDuration(d);

    public IEnumerable<TestNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(c => c.SelfAndDescendants()))
        {
            yield return descendant;
        }
    }

    public IEnumerable<TestNodeViewModel> Leaves() => SelfAndDescendants().Where(n => n.IsLeaf);

    /// <summary>Failed wins over running, running over not run, not run over skipped, skipped over passed.</summary>
    public void Aggregate()
    {
        if (IsLeaf)
        {
            return;
        }

        foreach (var child in Children)
        {
            child.Aggregate();
        }

        var outcomes = Children.Select(c => c.Outcome).ToList();
        Outcome = Combine(outcomes);
    }

    public TestNodeViewModel GetOrAdd(TestNodeKind kind, string name, string fullName)
    {
        if (Children.FirstOrDefault(c => c.Kind == kind && c.FullName == fullName) is { } existing)
        {
            return existing;
        }

        var node = new TestNodeViewModel(kind, name, fullName, ProjectPath, this);
        var index = 0;
        while (index < Children.Count && string.Compare(Children[index].Name, name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            index++;
        }

        Children.Insert(index, node);
        return node;
    }

    private static string FormatDuration(TimeSpan d) => d.TotalMilliseconds < 1000 ? $"{d.TotalMilliseconds:0} ms" : $"{d.TotalSeconds:0.0} s";

    private static TestOutcome Combine(IReadOnlyList<TestOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return TestOutcome.NotRun;
        }

        foreach (var priority in new[] { TestOutcome.Failed, TestOutcome.Running, TestOutcome.NotRun })
        {
            if (outcomes.Contains(priority))
            {
                return priority;
            }
        }

        return outcomes.All(o => o == TestOutcome.Skipped) ? TestOutcome.Skipped : TestOutcome.Passed;
    }
}
