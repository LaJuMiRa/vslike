using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VsLike.Core.Code;

namespace VsLike.Roslyn.Features;

/// <summary>
/// Parameterhilfe über das öffentliche Semantikmodell (Roslyns SignatureHelp-Dienst ist intern):
/// Überladungen des aufgerufenen Members und Index des Parameters unter dem Cursor.
/// </summary>
internal static class SignatureHelpFeature
{
    private static readonly SymbolDisplayFormat ParameterFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    public static async Task<SignatureHelp?> GetAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return null;
        }

        var token = root.FindToken(Math.Max(0, offset - 1));
        var argumentList = token.Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>()
            .FirstOrDefault(a => offset > a.OpenParenToken.SpanStart && (a.CloseParenToken.IsMissing || offset <= a.CloseParenToken.SpanStart));
        if (argumentList is null)
        {
            return null;
        }

        var methods = CandidateMethods(model, argumentList, cancellationToken);
        if (methods.Count == 0)
        {
            return null;
        }

        var activeParameter = argumentList.Arguments.GetSeparators().Count(s => s.SpanStart < offset);
        var resolved = argumentList.Parent is { } call ? model.GetSymbolInfo(call, cancellationToken).Symbol as IMethodSymbol : null;
        var activeSignature = resolved is not null ? methods.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, resolved)) : -1;
        if (activeSignature < 0)
        {
            activeSignature = Math.Max(0, methods.FindIndex(m => m.Parameters.Length > activeParameter || m.Parameters.LastOrDefault()?.IsParams == true));
        }

        return new SignatureHelp([.. methods.Select(Describe)], activeSignature, activeParameter);
    }

    private static List<IMethodSymbol> CandidateMethods(SemanticModel model, ArgumentListSyntax argumentList, CancellationToken cancellationToken)
    {
        IEnumerable<IMethodSymbol> methods = argumentList.Parent switch
        {
            InvocationExpressionSyntax invocation => model.GetMemberGroup(invocation.Expression, cancellationToken).OfType<IMethodSymbol>(),
            BaseObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation, cancellationToken).Type is INamedTypeSymbol type => type.InstanceConstructors,
            ConstructorInitializerSyntax initializer when model.GetSymbolInfo(initializer, cancellationToken) is var info => info.Symbol is IMethodSymbol m ? m.ContainingType.InstanceConstructors : info.CandidateSymbols.OfType<IMethodSymbol>(),
            _ => [],
        };
        var position = argumentList.SpanStart;
        return [.. methods.Where(m => model.IsAccessible(position, m)).OrderBy(m => m.Parameters.Length)];
    }

    private static SignatureInfo Describe(IMethodSymbol method)
    {
        var name = method.MethodKind == MethodKind.Constructor ? method.ContainingType.ToDisplayString(TypeFormat) : method.ToDisplayString(TypeFormat).Split('(')[0];
        var prefix = method.MethodKind == MethodKind.Constructor ? $"{name}(" : $"{method.ReturnType.ToDisplayString(TypeFormat)} {name}(";
        var parameters = method.Parameters.Select(p => p.ToDisplayString(ParameterFormat)).ToList();
        return new SignatureInfo(prefix, parameters, ")", Summary(method.GetDocumentationCommentXml()));
    }

    private static string? Summary(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var summary = XElement.Parse(xml).Element("summary");
            return summary is null ? null : string.Join(' ', summary.Value.Split((char[])['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
