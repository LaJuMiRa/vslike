using VsLike.Core.Commands;

namespace VsLike.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Score_ReturnsNullWhenNotASubsequence()
    {
        Assert.Null(FuzzyMatcher.Score("xyz", "Open File"));
    }

    [Fact]
    public void Score_PrefersWordStarts()
    {
        var wordStart = FuzzyMatcher.Score("of", "Open File");
        var middle = FuzzyMatcher.Score("of", "Toggle Profiler");

        Assert.NotNull(wordStart);
        Assert.NotNull(middle);
        Assert.True(wordStart > middle);
    }

    [Fact]
    public void Score_IsCaseInsensitive()
    {
        Assert.NotNull(FuzzyMatcher.Score("THEME", "Theme: Dark"));
    }
}
