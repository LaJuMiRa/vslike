namespace VsLike.App.Search;

public sealed record SearchResultLine(string FilePath, int Line, int Column, string Preview)
{
    public string Location => $"{Line}:{Column}";
}
