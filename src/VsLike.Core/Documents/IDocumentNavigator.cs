namespace VsLike.Core.Documents;

/// <summary>Öffnet Dateien im Editor; wird von Fehlerliste, Suche, Go to Definition usw. genutzt.</summary>
public interface IDocumentNavigator
{
    /// <param name="line">1-based line, optional.</param>
    /// <param name="column">1-based column, optional.</param>
    Task OpenAsync(string path, int? line = null, int? column = null);
}
