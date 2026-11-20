using VsLike.Core.Localization;

namespace VsLike.Core.Tests;

public class LocTests
{
    [Fact]
    public void Get_ReturnsGermanAndEnglishTexts()
    {
        Loc.SetCulture("de");
        Assert.Equal("Datei", Loc.Get("Menu.File"));

        Loc.SetCulture("en");
        Assert.Equal("File", Loc.Get("Menu.File"));
    }

    [Fact]
    public void Get_ReturnsKeyForMissingEntry()
    {
        Assert.Equal("Does.Not.Exist", Loc.Get("Does.Not.Exist"));
    }
}
