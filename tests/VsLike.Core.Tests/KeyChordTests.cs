using VsLike.Core.Commands;
using VsLike.Core.Platform;

namespace VsLike.Core.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("Cmd+Shift+P", ChordModifiers.Primary | ChordModifiers.Shift, "P")]
    [InlineData("F5", ChordModifiers.None, "F5")]
    [InlineData("ctrl+alt+k", ChordModifiers.Control | ChordModifiers.Alt, "K")]
    public void Parse_ReadsModifiersAndKey(string text, ChordModifiers modifiers, string key)
    {
        var chord = KeyChord.Parse(text);

        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(key, chord.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+P")]
    [InlineData("Cmd+")]
    public void TryParse_RejectsInvalidInput(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Fact]
    public void ToDisplayString_UsesSymbolsOnMacAndNamesOnLinux()
    {
        var chord = KeyChord.Parse("Cmd+Shift+P");

        Assert.Equal("⇧⌘P", chord.ToDisplayString(PlatformKind.MacOS));
        Assert.Equal("Ctrl+Shift+P", chord.ToDisplayString(PlatformKind.Linux));
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        var chord = KeyChord.Parse("Cmd+Alt+Shift+F");

        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }
}
