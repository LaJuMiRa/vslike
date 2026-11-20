using VsLike.Core.Code;

namespace VsLike.App.Problems;

public sealed record ProblemItem(DiagnosticInfo Diagnostic, string DisplayFile)
{
    public string SeverityGlyph => Diagnostic.Severity switch
    {
        DiagnosticSeverityKind.Error => "●",
        DiagnosticSeverityKind.Warning => "▲",
        _ => "◆",
    };

    public string SeverityClass => Diagnostic.Severity.ToString().ToLowerInvariant();

    public string Location => Diagnostic.FilePath is null ? string.Empty : $"{DisplayFile}:{Diagnostic.StartLine}:{Diagnostic.StartColumn}";
}
