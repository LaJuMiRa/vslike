namespace VsLike.Core.Code;

/// <summary>Eine Diagnose (Compiler, Analyzer, Build) unabhängig von Roslyn-Typen.</summary>
/// <param name="Start">0-based character offset in the document.</param>
/// <param name="StartLine">1-based line.</param>
/// <param name="StartColumn">1-based column.</param>
/// <param name="Category">Display category, e.g. "Compiler", "StyleCop", "Build".</param>
public sealed record DiagnosticInfo(
    string Id,
    string Message,
    DiagnosticSeverityKind Severity,
    string? FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartColumn,
    string Category,
    string? ProjectName = null,
    string? HelpLink = null);
