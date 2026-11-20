using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace VsLike.Editor.Highlighting;

/// <summary>
/// Grammatiken aus TextMateSharp.Grammars, Farben aus den eigenen VsLike-Themes (eingebettete JSON-Dateien).
/// </summary>
public sealed class VsLikeRegistryOptions : IRegistryOptions
{
    public const string DarkThemeName = "vslike-dark";
    public const string LightThemeName = "vslike-light";

    private static readonly Lazy<IRawTheme> DarkTheme = new(() => LoadTheme(DarkThemeName));
    private static readonly Lazy<IRawTheme> LightTheme = new(() => LoadTheme(LightThemeName));

    private readonly RegistryOptions _grammars = new(ThemeName.Dark);

    public static IRawTheme GetTheme(EditorThemeKind kind) => kind == EditorThemeKind.Dark ? DarkTheme.Value : LightTheme.Value;

    public IRawTheme GetTheme(string scopeName) => scopeName == LightThemeName ? LightTheme.Value : DarkTheme.Value;

    public IRawGrammar GetGrammar(string scopeName) => _grammars.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName) => _grammars.GetInjections(scopeName);

    public IRawTheme GetDefaultTheme() => DarkTheme.Value;

    private static IRawTheme LoadTheme(string name)
    {
        using var stream = typeof(VsLikeRegistryOptions).Assembly.GetManifestResourceStream($"VsLike.Editor.Highlighting.Themes.{name}.json")
            ?? throw new InvalidOperationException($"Theme resource '{name}' missing.");
        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
    }
}
