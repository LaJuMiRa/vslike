using System.Text.Json;
using System.Text.Json.Nodes;

namespace VsLike.Kit;

/// <summary>
/// The <c>stylecop.json</c> VsLike creates next to a solution: company name and copyright text for the file header rules
/// (SA1633 ff.). StyleCop replaces <c>{companyName}</c> and <c>{fileName}</c> in the copyright text.
/// </summary>
public static class StyleCopSettingsFile
{
    public const string FileName = "stylecop.json";
    public const string DefaultCopyrightText = "Copyright (c) {companyName}. All rights reserved.";
    internal const string Schema = "https://raw.githubusercontent.com/DotNetAnalyzers/StyleCopAnalyzers/master/StyleCop.Analyzers/StyleCop.Analyzers/Settings/stylecop.schema.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Where the file lives for a solution (or project) file: in its folder, where the engine finds it for every project.</summary>
    public static string Path(string solution) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(solution) ?? "/", FileName);

    public static string Contents(string companyName, string copyrightText)
    {
        // Keys sorted like the Swift original ("$schema" before "settings").
        var settings = new JsonObject
        {
            ["$schema"] = Schema,
            ["settings"] = new JsonObject
            {
                ["documentationRules"] = new JsonObject
                {
                    ["companyName"] = companyName,
                    ["copyrightText"] = copyrightText.Length == 0 ? DefaultCopyrightText : copyrightText,
                },
            },
        };
        return settings.ToJsonString(Options) + "\n";
    }
}
