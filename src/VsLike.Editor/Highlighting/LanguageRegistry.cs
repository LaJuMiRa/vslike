namespace VsLike.Editor.Highlighting;

/// <summary>Ordnet Dateien einer Sprache zu (TextMate-Scope, Kommentarzeichen).</summary>
public static class LanguageRegistry
{
    public static readonly EditorLanguage CSharp = new("csharp", "source.cs", "//", null);
    public static readonly EditorLanguage Xml = new("xml", "text.xml", null, ("<!--", "-->"));
    public static readonly EditorLanguage Json = new("json", "source.json.comments", "//", null);
    public static readonly EditorLanguage Markdown = new("markdown", "text.html.markdown", null, ("<!--", "-->"));
    public static readonly EditorLanguage PlainText = new("plaintext", null, null, null);

    private static readonly Dictionary<string, EditorLanguage> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CSharp,
        [".csx"] = CSharp,
        [".csproj"] = Xml,
        [".slnx"] = Xml,
        [".props"] = Xml,
        [".targets"] = Xml,
        [".xml"] = Xml,
        [".axaml"] = Xml,
        [".xaml"] = Xml,
        [".resx"] = Xml,
        [".nuspec"] = Xml,
        [".config"] = Xml,
        [".json"] = Json,
        [".jsonc"] = Json,
        [".md"] = Markdown,
        [".markdown"] = Markdown,
    };

    public static EditorLanguage FromPath(string? path)
        => path is not null && ByExtension.TryGetValue(Path.GetExtension(path), out var language) ? language : PlainText;
}
