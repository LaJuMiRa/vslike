using System.Globalization;

namespace VsLike.Core.Localization;

/// <summary>
/// The few texts the engine produces itself (all other UI texts live in the Swift app). Roslyn's own messages follow
/// <see cref="CultureInfo.CurrentUICulture"/>, which the app sets at start-up.
/// </summary>
public static class EngineText
{
    private static readonly Dictionary<string, (string En, string De)> Texts = new(StringComparer.Ordinal)
    {
        ["CodeAction.FixAllDocument"] = ("Fix all in document: {0}", "Alle im Dokument beheben: {0}"),
        ["CodeAction.FixAllProject"] = ("Fix all in project: {0}", "Alle im Projekt beheben: {0}"),
        ["CodeAction.AddDocumentation"] = ("Add documentation comment", "Dokumentationskommentar hinzufügen"),
    };

    /// <summary>Gets a value indicating whether German texts are used.</summary>
    public static bool IsGerman => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    /// <summary>Sets the culture for engine texts and Roslyn messages ("de", "en" or empty for the system culture).</summary>
    public static void SetCulture(string? language)
    {
        var culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Format(string key, params object[] args)
    {
        var text = Texts.TryGetValue(key, out var pair) ? (IsGerman ? pair.De : pair.En) : key;
        return string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
