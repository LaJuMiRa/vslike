using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// Find (and replace) in all files of the open folder. Runs off the UI thread; open documents use their unsaved text.
/// </summary>
public sealed partial class SearchModel : ObservableObject
{
    private readonly Workspace _workspace;
    private CancellationTokenSource? _task;

    public SearchModel(Workspace workspace)
    {
        _workspace = workspace;
    }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Replacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CaseSensitive { get; set; }

    [ObservableProperty]
    public partial bool WholeWord { get; set; }

    [ObservableProperty]
    public partial bool UseRegex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchCount))]
    public partial IReadOnlyList<FileResult> Results { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    public partial string? ErrorText { get; private set; }

    public int MatchCount => Results.Sum(r => r.Matches.Count);

    public void Run()
    {
        _task?.Cancel();
        ErrorText = null;
        if (_workspace.RootPath is null)
        {
            return;
        }

        Regex? regex;
        try
        {
            regex = Expression();
        }
        catch (ArgumentException)
        {
            ErrorText = L.T("Invalid regular expression");
            return;
        }

        if (regex is null)
        {
            Results = [];
            return;
        }

        var openTexts = new Dictionary<string, string>();
        foreach (var document in _workspace.OpenDocuments)
        {
            openTexts.TryAdd(document.Path, document.Text);
        }

        var files = FileTreeBuilder.AllFiles(_workspace.FolderTree).Select(n => n.Path).OfType<string>().ToList();
        IsSearching = true;
        var cancellation = new CancellationTokenSource();
        _task = cancellation;
        _ = SearchInBackground(regex, files, openTexts, cancellation.Token);
    }

    /// <summary>Replaces all matches in all files (open documents undoably).</summary>
    public void ReplaceAll()
    {
        Regex? regex;
        try
        {
            regex = Expression();
        }
        catch (ArgumentException)
        {
            return;
        }

        if (regex is null)
        {
            return;
        }

        var template = UseRegex ? Replacement : Replacement.Replace("$", "$$", StringComparison.Ordinal);
        foreach (var file in Results)
        {
            if (_workspace.OpenDocuments.FirstOrDefault(d => d.Path == file.Path) is { } document)
            {
                var replaced = regex.Replace(document.Text, template);
                document.Replace(new TextRange(0, document.Storage.TextLength), replaced, L.T("Replace All"));
            }
            else if (ReadText(file.Path) is { } text)
            {
                try
                {
                    File.WriteAllText(file.Path, regex.Replace(text, template));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // try? in the original.
                }
            }
        }

        Run();
    }

    private static string? ReadText(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length >= 4_000_000)
            {
                return null;
            }

            var data = File.ReadAllBytes(path);
            if (data.Take(1024).Contains((byte)0))
            {
                return null;
            }

            return new System.Text.UTF8Encoding(false, true).GetString(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    private static List<Match> Matches(Regex regex, string text, string path)
    {
        var result = new List<Match>();
        var lineNumber = 1;
        var lineStart = 0;
        var scanned = 0;
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(text))
        {
            if (match.Length == 0)
            {
                continue;
            }

            while (scanned < match.Index)
            {
                if (text[scanned] == '\n')
                {
                    lineNumber++;
                    lineStart = scanned + 1;
                }

                scanned++;
            }

            var line = TextEditing.LineRange(text, lineStart);
            var preview = text.Substring(line.Location, line.Length).Trim();
            result.Add(new Match($"{path}:{match.Index}", lineNumber, match.Index - lineStart + 1, match.Length, preview));
        }

        return result;
    }

    private Regex? Expression()
    {
        if (Query.Length == 0)
        {
            return null;
        }

        var pattern = UseRegex ? Query : Regex.Escape(Query);
        if (WholeWord)
        {
            pattern = @"\b" + pattern + @"\b";
        }

        return new Regex(pattern, CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
    }

    private async Task SearchInBackground(Regex regex, List<string> files, Dictionary<string, string> openTexts, CancellationToken token)
    {
        var sorted = await Task.Run(
            () =>
            {
                var found = new List<FileResult>();
                var total = 0;
                foreach (var path in files)
                {
                    if (token.IsCancellationRequested)
                    {
                        return null;
                    }

                    var text = openTexts.GetValueOrDefault(path) ?? ReadText(path);
                    if (text is null)
                    {
                        continue;
                    }

                    var matches = Matches(regex, text, path);
                    if (matches.Count > 0)
                    {
                        found.Add(new FileResult(path, matches));
                        total += matches.Count;
                    }

                    if (total > 5000)
                    {
                        break;
                    }
                }

                return found.OrderBy(f => f.Path, FileTreeBuilder.StandardOrder).ToList();
            },
            CancellationToken.None);
        if (sorted is null || token.IsCancellationRequested)
        {
            return;
        }

        Results = sorted;
        IsSearching = false;
    }

    public sealed record Match(string Id, int Line, int Column, int Length, string Preview);

    public sealed record FileResult(string Path, IReadOnlyList<Match> Matches)
    {
        public string Id => Path;
    }
}
