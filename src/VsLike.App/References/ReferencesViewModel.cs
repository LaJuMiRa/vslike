using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.App.Search;
using VsLike.Core.Code;
using VsLike.Core.Documents;
using VsLike.Core.Localization;

namespace VsLike.App.References;

/// <summary>Ergebnisliste von „Alle Verweise suchen“, gruppiert nach Datei.</summary>
public sealed partial class ReferencesViewModel : ObservableObject
{
    private readonly IDocumentNavigator _navigator;

    [ObservableProperty]
    private string _summary = string.Empty;

    public ReferencesViewModel(IDocumentNavigator navigator)
    {
        _navigator = navigator;
    }

    public ObservableCollection<SearchResultFile> Results { get; } = [];

    public void Show(string symbol, IReadOnlyList<SourceLocation> locations, string? root)
    {
        Results.Clear();
        foreach (var group in locations.GroupBy(l => l.FilePath))
        {
            var display = root is not null && group.Key.StartsWith(root, StringComparison.Ordinal) ? Path.GetRelativePath(root, group.Key) : group.Key;
            Results.Add(new SearchResultFile(group.Key, display, [.. group.Select(l => new SearchResultLine(l.FilePath, l.Line, l.Column, l.LineText))]));
        }

        Summary = Loc.Format("References.Summary", locations.Count, symbol);
    }

    public Task OpenAsync(SearchResultLine line) => _navigator.OpenAsync(line.FilePath, line.Line, line.Column);
}
