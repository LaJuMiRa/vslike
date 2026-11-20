using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using VsLike.Core.Code;

namespace VsLike.Roslyn;

/// <summary>Übersetzt Roslyn-Typen in die Roslyn-freien Core-Typen.</summary>
internal static class RoslynMapping
{
    private static readonly Dictionary<string, string> Classifications = new(StringComparer.Ordinal)
    {
        [ClassificationTypeNames.ClassName] = SemanticClassifications.Class,
        [ClassificationTypeNames.RecordClassName] = SemanticClassifications.Record,
        [ClassificationTypeNames.StructName] = SemanticClassifications.Struct,
        [ClassificationTypeNames.RecordStructName] = SemanticClassifications.Struct,
        [ClassificationTypeNames.InterfaceName] = SemanticClassifications.Interface,
        [ClassificationTypeNames.EnumName] = SemanticClassifications.Enum,
        [ClassificationTypeNames.DelegateName] = SemanticClassifications.Delegate,
        [ClassificationTypeNames.TypeParameterName] = SemanticClassifications.TypeParameter,
        [ClassificationTypeNames.MethodName] = SemanticClassifications.Method,
        [ClassificationTypeNames.ExtensionMethodName] = SemanticClassifications.ExtensionMethod,
        [ClassificationTypeNames.PropertyName] = SemanticClassifications.Property,
        [ClassificationTypeNames.FieldName] = SemanticClassifications.Field,
        [ClassificationTypeNames.ConstantName] = SemanticClassifications.Constant,
        [ClassificationTypeNames.EnumMemberName] = SemanticClassifications.EnumMember,
        [ClassificationTypeNames.EventName] = SemanticClassifications.Event,
        [ClassificationTypeNames.ParameterName] = SemanticClassifications.Parameter,
        [ClassificationTypeNames.LocalName] = SemanticClassifications.Local,
        [ClassificationTypeNames.NamespaceName] = SemanticClassifications.Namespace,
    };

    public static string? MapClassification(string roslynName) => Classifications.GetValueOrDefault(roslynName);

    public static DiagnosticSeverityKind MapSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => DiagnosticSeverityKind.Error,
        DiagnosticSeverity.Warning => DiagnosticSeverityKind.Warning,
        DiagnosticSeverity.Info => DiagnosticSeverityKind.Info,
        _ => DiagnosticSeverityKind.Hidden,
    };

    public static DiagnosticInfo ToInfo(Diagnostic diagnostic, string? projectName)
    {
        var span = diagnostic.Location.GetLineSpan();
        var isSource = diagnostic.Location.IsInSource;
        return new DiagnosticInfo(
            diagnostic.Id,
            diagnostic.GetMessage(System.Globalization.CultureInfo.CurrentUICulture),
            MapSeverity(diagnostic.Severity),
            isSource ? span.Path : null,
            isSource ? diagnostic.Location.SourceSpan.Start : 0,
            isSource ? diagnostic.Location.SourceSpan.Length : 0,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            CategoryOf(diagnostic),
            projectName,
            diagnostic.Descriptor.HelpLinkUri is { Length: > 0 } link ? link : null);
    }

    private static string CategoryOf(Diagnostic diagnostic)
    {
        if (diagnostic.Id.StartsWith("SA", StringComparison.Ordinal) || diagnostic.Id.StartsWith("SX", StringComparison.Ordinal))
        {
            return "StyleCop";
        }

        return diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.Compiler) ? "Compiler" : "Analyzer";
    }
}
