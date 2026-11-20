using System.Globalization;
using System.Resources;

namespace VsLike.Core.Localization;

/// <summary>
/// Zugriff auf die UI-Texte (Strings.resx = Englisch, Strings.de.resx = Deutsch).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("VsLike.Core.Localization.Strings", typeof(Loc).Assembly);

    public static event EventHandler? CultureChanged;

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Returns the text for <paramref name="key"/>, or the key itself if it is missing.</summary>
    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] args) => string.Format(Culture, Get(key), args);

    /// <summary>Sets the UI culture ("de", "en" or empty for the system culture).</summary>
    public static void SetCulture(string language)
    {
        Culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(language);

        // Auch Compiler-/Analyzer-Meldungen (Roslyn) und das dotnet-CLI folgen der UI-Sprache.
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }
}
