using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App.Problems;

/// <summary>
/// Fehlerliste: sammelt Diagnosen aus mehreren Quellen (Live-Analyse, Build) und filtert nach Schweregrad und Text.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    public const string LiveSource = "live";
    public const string BuildSource = "build";

    private readonly IDocumentNavigator _navigator;
    private readonly Dictionary<(string Source, string File), IReadOnlyList<DiagnosticInfo>> _entries = [];
    private string? _rootPath;

    [ObservableProperty]
    private bool _showErrors = true;

    [ObservableProperty]
    private bool _showWarnings = true;

    [ObservableProperty]
    private bool _showMessages = true;

    [ObservableProperty]
    private string _filter = string.Empty;

    public ProblemsViewModel(IDocumentNavigator navigator)
    {
        _navigator = navigator;
        Loc.CultureChanged += (_, _) => RaiseCounts();
    }

    public event EventHandler? CountsChanged;

    public ObservableCollection<ProblemItem> Items { get; } = [];

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int MessageCount { get; private set; }

    public string ErrorsLabel => Loc.Format("Problems.Errors", ErrorCount);

    public string WarningsLabel => Loc.Format("Problems.Warnings", WarningCount);

    public string MessagesLabel => Loc.Format("Problems.Messages", MessageCount);

    public bool IsEmpty => Items.Count == 0;

    public IEnumerable<DiagnosticInfo> All => _entries.Values.SelectMany(v => v);

    /// <summary>Sets the folder used to display relative file paths.</summary>
    public void SetRoot(string? rootPath)
    {
        _rootPath = rootPath;
        Rebuild();
    }

    /// <summary>Replaces all diagnostics of one source (e.g. after a solution-wide analysis or a build).</summary>
    public void ReplaceSource(string source, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        foreach (var key in _entries.Keys.Where(k => k.Source == source).ToList())
        {
            _entries.Remove(key);
        }

        foreach (var group in diagnostics.GroupBy(d => d.FilePath ?? string.Empty))
        {
            _entries[(source, group.Key)] = [.. group];
        }

        Rebuild();
    }

    /// <summary>Replaces the diagnostics of one file from one source.</summary>
    /// <remarks>
    /// A live update supersedes the compiler errors of the last build for that file (they would be stale or duplicates);
    /// other build diagnostics (NuGet, MSBuild, analyzers that only run in the build) stay.
    /// </remarks>
    public void ReplaceFile(string source, string filePath, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        _entries[(source, filePath)] = diagnostics;
        if (source == LiveSource && _entries.TryGetValue((BuildSource, filePath), out var build))
        {
            _entries[(BuildSource, filePath)] = [.. build.Where(d => !d.Id.StartsWith("CS", StringComparison.Ordinal))];
        }

        Rebuild();
    }

    public void Clear()
    {
        _entries.Clear();
        Rebuild();
    }

    public Task OpenAsync(ProblemItem item)
        => item.Diagnostic.FilePath is { } path ? _navigator.OpenAsync(path, item.Diagnostic.StartLine, item.Diagnostic.StartColumn) : Task.CompletedTask;

    partial void OnShowErrorsChanged(bool value) => Rebuild();

    partial void OnShowWarningsChanged(bool value) => Rebuild();

    partial void OnShowMessagesChanged(bool value) => Rebuild();

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var all = _entries.Values.SelectMany(v => v).DistinctBy(d => (d.Id, d.FilePath, d.StartLine, d.StartColumn, d.Message)).ToList();
        ErrorCount = all.Count(d => d.Severity == DiagnosticSeverityKind.Error);
        WarningCount = all.Count(d => d.Severity == DiagnosticSeverityKind.Warning);
        MessageCount = all.Count(d => d.Severity == DiagnosticSeverityKind.Info);

        var visible = all
            .Where(d => d.Severity switch
            {
                DiagnosticSeverityKind.Error => ShowErrors,
                DiagnosticSeverityKind.Warning => ShowWarnings,
                DiagnosticSeverityKind.Info => ShowMessages,
                _ => false,
            })
            .Where(d => Filter.Length == 0
                || d.Message.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
                || d.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (d.FilePath?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.FilePath, StringComparer.Ordinal)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn);

        Items.Clear();
        foreach (var diagnostic in visible)
        {
            Items.Add(new ProblemItem(diagnostic, DisplayPath(diagnostic.FilePath)));
        }

        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(ErrorsLabel));
        OnPropertyChanged(nameof(WarningsLabel));
        OnPropertyChanged(nameof(MessagesLabel));
        OnPropertyChanged(nameof(IsEmpty));
        CountsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string DisplayPath(string? path)
        => path is null ? string.Empty : _rootPath is not null && path.StartsWith(_rootPath, StringComparison.Ordinal) ? Path.GetRelativePath(_rootPath, path) : Path.GetFileName(path);
}
