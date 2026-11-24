using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace VsLike.Desktop;

/// <summary>
/// Localized texts (no Swift original; replaces <c>String(localized:)</c>). Keys are the keys of
/// <c>Sources/VsLike/Resources/*.lproj/Localizable.strings</c>: the English text with printf placeholders, e.g.
/// <c>L.T("%lld projects loaded", count)</c> or <c>L.T("Saved %@", name)</c>. Values in <c>Resources/Strings*.resx</c> use
/// .NET placeholders. A key without an entry falls back to the key itself (English), like on macOS.
/// </summary>
public static partial class L
{
    // Case-insensitive: Localizable.strings has keys that differ only in case ("Base class", "Base Class").
    private static readonly ResourceManager Resources = new("VsLike.Desktop.Resources.Strings", typeof(L).Assembly) { IgnoreCase = true };

    /// <summary>Gets or sets the UI culture ("en" or "de"); set once at startup from <see cref="Preferences.EffectiveLanguage"/>.</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en");

    public static string T(string key, params object?[] args)
    {
        var format = Resources.GetString(key, Culture) ?? Convert(key);
        try
        {
            return string.Format(Culture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>Converts a printf-style key (<c>%@</c>, <c>%lld</c>, <c>%d</c>, <c>%1$@</c>) into a .NET format string.</summary>
    internal static string Convert(string key)
    {
        var escaped = key.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        var index = 0;
        return Placeholder().Replace(escaped, match =>
        {
            if (match.Groups[2].Value == "%")
            {
                return "%";
            }

            var position = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1 : index++;
            return "{" + position.ToString(CultureInfo.InvariantCulture) + "}";
        });
    }

    [GeneratedRegex(@"%(?:(\d+)\$)?(@|lld|ld|d|%)")]
    private static partial Regex Placeholder();
}
