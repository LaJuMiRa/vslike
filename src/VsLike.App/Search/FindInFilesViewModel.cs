using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VsLike.App.Editor;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Core.Search;

namespace VsLike.App.Search;

/// <summary>Suchen und Ersetzen über alle Dateien des geöffneten Ordners bzw. der Solution.</summary>
public sealed partial class FindInFilesViewModel : ObservableObject
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private readonly Func<IReadOnlyList<string>> _files;
    private readonly Func<string?> _root;
    private readonly IDocumentNavigator _navigator;
    private readonly DocumentManager _documents;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _replacement = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _summary = string.Empty;

    public FindInFilesViewModel(Func<IReadOnlyList<string>> files, Func<string?> root, IDocumentNavigator navigator, DocumentManager documents)
    {
        _files = files;
        _root = root;
        _navigator = navigator;
        _documents = documents;
    }

    /// <summary>Raised when the panel should take keyboard focus (Find in Files command).</summary>
    public event EventHandler? FocusRequested;

    public ObservableCollection<SearchResultFile> Results { get; } = [];

    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public async Task SearchAsync()
    {
        _cancellation?.Cancel();
        var cancellation = _cancellation = new CancellationTokenSource();
        Results.Clear();
        if (!TryCreateOptions(out var options))
        {
            return;
        }

        var matches = await TextSearch.SearchFilesAsync(CandidateFiles(), options, cancellation.Token).ConfigureAwait(true);
        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        var root = _root();
        foreach (var group in matches.GroupBy(m => m.FilePath))
        {
            var display = root is null ? group.Key : Path.GetRelativePath(root, group.Key);
            Results.Add(new SearchResultFile(group.Key, display, [.. group.Select(m => new SearchResultLine(m.FilePath, m.Match.Line, m.Match.Column, m.Match.LineText.Trim()))]));
        }

        Summary = Loc.Format("Search.Summary", matches.Count, Results.Count);
    }

    [RelayCommand]
    public async Task ReplaceAllAsync()
    {
        if (!TryCreateOptions(out var options))
        {
            return;
        }

        var files = 0;
        var total = 0;
        foreach (var path in CandidateFiles())
        {
            var open = _documents.OpenDocuments.FirstOrDefault(d => d.FilePath == path);
            var text = open?.TextDocument.Text ?? await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var (replaced, count) = TextSearch.ReplaceAll(text, options, Replacement);
            if (count == 0)
            {
                continue;
            }

            if (open is not null)
            {
                open.TextDocument.Replace(0, open.TextDocument.TextLength, replaced);
            }
            else
            {
                var (_, encoding) = await TextFileIO.ReadAsync(path).ConfigureAwait(true);
                await TextFileIO.WriteAsync(path, replaced, encoding).ConfigureAwait(true);
            }

            files++;
            total += count;
        }

        await SearchAsync().ConfigureAwait(true);
        Summary = Loc.Format("Search.Replaced", total, files);
    }

    public Task OpenAsync(SearchResultLine line) => _navigator.OpenAsync(line.FilePath, line.Line, line.Column);

    private IEnumerable<string> CandidateFiles()
        => _files().Where(f =>
        {
            try
            {
                return new FileInfo(f).Length <= MaxFileSize;
            }
            catch (IOException)
            {
                return false;
            }
        });

    private bool TryCreateOptions(out SearchOptions options)
    {
        options = new SearchOptions(Query, MatchCase, WholeWord, UseRegex);
        if (string.IsNullOrEmpty(Query))
        {
            Summary = string.Empty;
            return false;
        }

        if (_root() is null)
        {
            Summary = Loc.Get("Search.NoFolder");
            return false;
        }

        try
        {
            TextSearch.CreateRegex(options);
            return true;
        }
        catch (ArgumentException ex)
        {
            Summary = Loc.Format("Search.InvalidRegex", ex.Message);
            return false;
        }
    }
}
