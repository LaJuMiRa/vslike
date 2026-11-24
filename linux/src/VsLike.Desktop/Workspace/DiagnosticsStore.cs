using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// All problems of the solution: live diagnostics from the engine (per file, while typing) plus diagnostics of the
/// last build. A file with live diagnostics drops the build's compiler (CS) entries, which would be stale.
/// </summary>
public sealed partial class DiagnosticsStore : ObservableObject
{
    public enum Filter
    {
        All,
        Errors,
        Warnings,
        StyleCop,
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(All), nameof(Visible), nameof(ErrorCount), nameof(WarningCount), nameof(StyleCopCount))]
    public partial IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> Live { get; private set; } = new Dictionary<string, IReadOnlyList<Diagnostic>>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(All), nameof(Visible), nameof(ErrorCount), nameof(WarningCount), nameof(StyleCopCount))]
    public partial IReadOnlyList<Diagnostic> Build { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    public partial Filter CurrentFilter { get; set; } = Filter.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Gets the problems in display order: errors first, then by file and line. Hidden diagnostics are never shown.</summary>
    public IReadOnlyList<Diagnostic> All
    {
        get
        {
            var liveFiles = Live.Keys.ToHashSet();
            var fromBuild = Build.Where(d => !(d.Code.StartsWith("CS", StringComparison.Ordinal) && d.FilePath is not null && liveFiles.Contains(d.FilePath)));
            var seen = new HashSet<string>();
            return Live.Values.SelectMany(l => l).Concat(fromBuild)
                .Where(d => d.Severity != DiagnosticSeverity.Hidden && seen.Add(Key(d)))
                .OrderByDescending(d => d.Severity)
                .ThenBy(d => d.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(d => d.StartLine)
                .ThenBy(d => d.StartColumn).ToList();
        }
    }

    public IReadOnlyList<Diagnostic> Visible
    {
        get
        {
            var needle = Search.Trim().ToLowerInvariant();
            return All.Where(d => Matches(d) && (needle.Length == 0
                || d.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || d.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (d.FilePath?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
        }
    }

    public int ErrorCount => All.Count(d => d.Severity == DiagnosticSeverity.Error);

    public int WarningCount => All.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public int StyleCopCount => All.Count(d => d.IsStyleCop);

    public IReadOnlyList<Diagnostic> DiagnosticsFor(string path) =>
        Live.TryGetValue(path, out var list)
            ? [.. list.Where(d => d.Severity != DiagnosticSeverity.Hidden)]
            : [.. Build.Where(d => d.FilePath == path)];

    public void SetLive(IReadOnlyList<Diagnostic> diagnostics, string? path)
    {
        if (path is not null)
        {
            Live = new Dictionary<string, IReadOnlyList<Diagnostic>>(Live) { [path] = diagnostics };
        }
        else
        {
            Live = diagnostics.Where(d => d.FilePath is not null).GroupBy(d => d.FilePath!).ToDictionary(g => g.Key, g => (IReadOnlyList<Diagnostic>)[.. g]);
        }
    }

    public void SetBuild(IReadOnlyList<Diagnostic> diagnostics)
    {
        Build = diagnostics;
    }

    public void Clear()
    {
        Live = new Dictionary<string, IReadOnlyList<Diagnostic>>();
        Build = [];
    }

    private static string Key(Diagnostic diagnostic) => $"{diagnostic.FilePath ?? string.Empty}:{diagnostic.StartLine}:{diagnostic.StartColumn}:{diagnostic.Code}";

    private bool Matches(Diagnostic diagnostic) => CurrentFilter switch
    {
        Filter.All => true,
        Filter.Errors => diagnostic.Severity == DiagnosticSeverity.Error,
        Filter.Warnings => diagnostic.Severity == DiagnosticSeverity.Warning && !diagnostic.IsStyleCop,
        _ => diagnostic.IsStyleCop,
    };
}
