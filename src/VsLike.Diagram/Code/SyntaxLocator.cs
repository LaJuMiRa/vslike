using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VsLike.Diagram.Code;

/// <summary>Findet Typ- und Mitgliedsdeklarationen in einem Syntaxbaum über den Diagramm-Schlüssel.</summary>
internal static class SyntaxLocator
{
    public static SyntaxNode Parse(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();

    /// <summary>Full name of a declaration: namespace, containing types and arity, e.g. <c>Shop.Outer.Repo`1</c>.</summary>
    public static string FullNameOf(BaseTypeDeclarationSyntax declaration)
    {
        var names = new List<string>();
        foreach (var node in declaration.AncestorsAndSelf())
        {
            switch (node)
            {
                case TypeDeclarationSyntax type:
                    names.Insert(0, type.Identifier.Text + (type.TypeParameterList is { Parameters.Count: > 0 } list ? "`" + list.Parameters.Count : string.Empty));
                    break;
                case EnumDeclarationSyntax enumeration:
                    names.Insert(0, enumeration.Identifier.Text);
                    break;
                case BaseNamespaceDeclarationSyntax ns:
                    names.Insert(0, ns.Name.ToString());
                    break;
            }
        }

        return string.Join('.', names);
    }

    public static BaseTypeDeclarationSyntax FindType(SyntaxNode root, string fullName)
        => root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(t => FullNameOf(t) == fullName)
            ?? throw new InvalidOperationException($"Type '{fullName}' not found.");

    public static IEnumerable<BaseTypeDeclarationSyntax> Types(SyntaxNode root) => root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();

    /// <summary>Signature as used by <see cref="Model.DiagramMember.Signature"/> (parameter types as written).</summary>
    public static string? SignatureOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => $"{m.Identifier.Text}({string.Join(",", m.ParameterList.Parameters.Select(p => p.Type?.ToString()))})",
        ConstructorDeclarationSyntax c => $"{c.Identifier.Text}({string.Join(",", c.ParameterList.Parameters.Select(p => p.Type?.ToString()))})",
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        EventFieldDeclarationSyntax e => e.Declaration.Variables.Count == 1 ? e.Declaration.Variables[0].Identifier.Text : null,
        FieldDeclarationSyntax f => f.Declaration.Variables.Count == 1 ? f.Declaration.Variables[0].Identifier.Text : null,
        EnumMemberDeclarationSyntax e => e.Identifier.Text,
        _ => null,
    };

    public static MemberDeclarationSyntax FindMember(BaseTypeDeclarationSyntax type, string signature)
    {
        IEnumerable<MemberDeclarationSyntax> members = type switch
        {
            TypeDeclarationSyntax t => t.Members,
            EnumDeclarationSyntax e => e.Members,
            _ => [],
        };
        return members.FirstOrDefault(m => SignatureOf(m) == signature) ?? throw new InvalidOperationException($"Member '{signature}' not found in '{FullNameOf(type)}'.");
    }
}
