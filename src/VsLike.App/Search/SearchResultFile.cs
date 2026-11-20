namespace VsLike.App.Search;

public sealed record SearchResultFile(string FilePath, string DisplayPath, IReadOnlyList<SearchResultLine> Matches);
