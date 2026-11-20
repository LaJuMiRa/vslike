using VsLike.Core.Search;

namespace VsLike.Core.Tests;

public sealed class TextSearchTests : IDisposable
{
    private const string Sample = "var foo = 1;\nvar Foo = foo + 2;\n// food\n";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vslike-search", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindAll_IsCaseInsensitiveByDefault_AndReportsLineAndColumn()
    {
        var matches = TextSearch.FindAll(Sample, new SearchOptions("foo"));

        Assert.Equal(4, matches.Count);
        Assert.Equal((2, 5), (matches[1].Line, matches[1].Column));
        Assert.Equal("var Foo = foo + 2;", matches[1].LineText);
    }

    [Fact]
    public void FindAll_RespectsMatchCaseAndWholeWord()
    {
        Assert.Single(TextSearch.FindAll(Sample, new SearchOptions("Foo", MatchCase: true)));
        Assert.Equal(3, TextSearch.FindAll(Sample, new SearchOptions("foo", WholeWord: true)).Count);
    }

    [Fact]
    public void FindAll_SupportsRegex()
    {
        var matches = TextSearch.FindAll(Sample, new SearchOptions(@"\d+", UseRegex: true));

        Assert.Equal(["1", "2"], matches.Select(m => Sample.Substring(m.Offset, m.Length)));
    }

    [Fact]
    public void ReplaceAll_UsesGroupsInRegexMode()
    {
        var (text, count) = TextSearch.ReplaceAll("a=1, b=2", new SearchOptions(@"(\w)=(\d)", UseRegex: true), "$2=$1");

        Assert.Equal("1=a, 2=b", text);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReplaceAll_TreatsDollarLiterallyWithoutRegex()
    {
        var (text, _) = TextSearch.ReplaceAll("price", new SearchOptions("price"), "$1");

        Assert.Equal("$1", text);
    }

    [Fact]
    public void CreateRegex_ThrowsForInvalidPattern()
    {
        Assert.ThrowsAny<ArgumentException>(() => TextSearch.CreateRegex(new SearchOptions("(", UseRegex: true)));
    }

    [Fact]
    public async Task SearchFilesAsync_FindsMatchesAcrossFilesAndSkipsBinary()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "a.cs"), "class Alpha {}\n");
        File.WriteAllText(Path.Combine(_directory, "b.cs"), "// Alpha\nclass Beta {}\n");
        File.WriteAllText(Path.Combine(_directory, "c.bin"), "Alpha\0\0");

        var results = await TextSearch.SearchFilesAsync(Directory.GetFiles(_directory), new SearchOptions("Alpha"), CancellationToken.None);

        Assert.Equal(["a.cs", "b.cs"], results.Select(r => Path.GetFileName(r.FilePath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
