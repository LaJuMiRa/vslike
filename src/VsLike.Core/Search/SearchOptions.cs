namespace VsLike.Core.Search;

public sealed record SearchOptions(string Query, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);
