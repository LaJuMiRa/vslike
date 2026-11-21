using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VsLike.Roslyn.Analyzers;

/// <summary>
/// Builds the lines of an XML documentation comment for a declaration, in the wording StyleCop expects
/// (SA1623 "Gets or sets …", SA1642 "Initializes a new instance of …", sentences ending with a period).
/// </summary>
internal static class DocumentationText
{
    private static readonly string[] PredicateWords = ["is", "has", "can", "should", "allows", "contains"];

    /// <summary>The comment lines without the leading <c>///</c>.</summary>
    public static IReadOnlyList<string> For(MemberDeclarationSyntax member, ISymbol? symbol)
    {
        if (symbol is not null && InheritsDocumentation(symbol))
        {
            return ["<inheritdoc/>"];
        }

        var lines = new List<string>();
        AddSummary(lines, Summary(member));
        AddTypeParameters(lines, member);
        AddParameters(lines, member);
        if (Returns(member) is { } returns)
        {
            lines.Add($"<returns>{returns}</returns>");
        }

        return lines;
    }

    /// <summary>Splits a PascalCase or camelCase name into lower-case words ("TotalPrice" becomes "total price").</summary>
    public static string Words(string name)
    {
        var builder = new StringBuilder();
        var trimmed = name.TrimStart('_', '@');
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            var startsWord = i > 0 && char.IsUpper(c) && (char.IsLower(trimmed[i - 1]) || (i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1])));
            if (startsWord || c == '_')
            {
                builder.Append(' ');
            }

            if (c != '_')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Trim();
    }

    private static bool InheritsDocumentation(ISymbol symbol)
    {
        if (symbol.IsOverride)
        {
            return true;
        }

        var explicitImplementations = symbol switch
        {
            IMethodSymbol method => method.ExplicitInterfaceImplementations.Length,
            IPropertySymbol property => property.ExplicitInterfaceImplementations.Length,
            IEventSymbol @event => @event.ExplicitInterfaceImplementations.Length,
            _ => 0,
        };
        if (explicitImplementations > 0)
        {
            return true;
        }

        return symbol.ContainingType is { } type && symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event
            && type.AllInterfaces.SelectMany(i => i.GetMembers()).Any(m => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(m), symbol));
    }

    private static void AddSummary(List<string> lines, string summary)
    {
        lines.Add("<summary>");
        lines.Add(summary);
        lines.Add("</summary>");
    }

    private static string Summary(MemberDeclarationSyntax member) => member switch
    {
        ConstructorDeclarationSyntax constructor when constructor.Modifiers.Any(SyntaxKind.StaticKeyword)
            => $"Initializes static members of the {TypeReference(constructor)} {TypeKindWord(constructor)}.",
        ConstructorDeclarationSyntax constructor => $"Initializes a new instance of the {TypeReference(constructor)} {TypeKindWord(constructor)}.",
        DestructorDeclarationSyntax destructor => $"Finalizes an instance of the {TypeReference(destructor)} class.",
        InterfaceDeclarationSyntax type => $"Defines the {Words(TrimInterfacePrefix(type.Identifier.Text))} contract.",
        EnumDeclarationSyntax type => $"Specifies the {Words(type.Identifier.Text)} values.",
        BaseTypeDeclarationSyntax type => $"Represents {Article(Words(type.Identifier.Text))}.",
        DelegateDeclarationSyntax @delegate => $"Represents the method that handles {Words(@delegate.Identifier.Text)}.",
        EnumMemberDeclarationSyntax value => $"The {Words(value.Identifier.Text)} value.",
        MethodDeclarationSyntax method => MethodSummary(TrimAsync(method.Identifier.Text)),
        IndexerDeclarationSyntax indexer => $"{Accessors(indexer.AccessorList)} the element at the specified index.",
        PropertyDeclarationSyntax property => PropertySummary(property),
        EventDeclarationSyntax @event => $"Occurs when {Words(@event.Identifier.Text)}.",
        EventFieldDeclarationSyntax @event => $"Occurs when {Words(@event.Declaration.Variables.First().Identifier.Text)}.",
        BaseFieldDeclarationSyntax field => $"The {Words(field.Declaration.Variables.First().Identifier.Text)}.",
        _ => "TODO.",
    };

    private static string MethodSummary(string name)
    {
        var words = Words(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "TODO.";
        }

        if (IsPredicate(words))
        {
            return $"Determines whether {string.Join(' ', words.Skip(1))}.";
        }

        var verb = ThirdPerson(words[0]);
        var verbText = char.ToUpperInvariant(verb[0]) + verb[1..];
        return words.Length == 1 ? $"{verbText}." : $"{verbText} the {string.Join(' ', words.Skip(1))}.";
    }

    private static string PropertySummary(PropertyDeclarationSyntax property)
    {
        var accessors = property.ExpressionBody is not null ? "Gets" : Accessors(property.AccessorList);
        var isBool = property.Type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword }
            || (property.Type is NullableTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword } });
        var words = Words(property.Identifier.Text);
        return isBool
            ? $"{accessors} a value indicating whether {StripPredicate(words)}."
            : $"{accessors} the {words}.";
    }

    private static string Accessors(AccessorListSyntax? list)
    {
        var kinds = list?.Accessors.Select(a => a.Kind()).ToList() ?? [];
        if (kinds.Contains(SyntaxKind.SetAccessorDeclaration))
        {
            return kinds.Contains(SyntaxKind.GetAccessorDeclaration) ? "Gets or sets" : "Sets";
        }

        return kinds.Contains(SyntaxKind.InitAccessorDeclaration) ? "Gets or initializes" : "Gets";
    }

    private static void AddTypeParameters(List<string> lines, MemberDeclarationSyntax member)
    {
        var parameters = member switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList,
            MethodDeclarationSyntax method => method.TypeParameterList,
            DelegateDeclarationSyntax @delegate => @delegate.TypeParameterList,
            _ => null,
        };
        foreach (var parameter in parameters?.Parameters ?? [])
        {
            var words = Words(parameter.Identifier.Text.TrimStart('T'));
            lines.Add($"<typeparam name=\"{parameter.Identifier.Text}\">The type of the {(words.Length > 0 ? words : "value")}.</typeparam>");
        }
    }

    private static void AddParameters(List<string> lines, MemberDeclarationSyntax member)
    {
        var parameters = member switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
            DelegateDeclarationSyntax @delegate => @delegate.ParameterList.Parameters,
            TypeDeclarationSyntax { ParameterList: { } primary } => primary.Parameters,
            _ => default,
        };
        foreach (var parameter in parameters)
        {
            lines.Add($"<param name=\"{parameter.Identifier.Text}\">The {Words(parameter.Identifier.Text)}.</param>");
        }
    }

    private static string? Returns(MemberDeclarationSyntax member)
    {
        var (type, name) = member switch
        {
            MethodDeclarationSyntax method => (method.ReturnType, TrimAsync(method.Identifier.Text)),
            DelegateDeclarationSyntax @delegate => (@delegate.ReturnType, @delegate.Identifier.Text),
            _ => (null, string.Empty),
        };
        if (type is null || type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword } || IsPlainTask(type))
        {
            return null;
        }

        var words = Words(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword } && IsPredicate(words))
        {
            return $"<c>true</c> if {string.Join(' ', words.Skip(1))}; otherwise, <c>false</c>.";
        }

        return words.Length > 1 ? $"The {string.Join(' ', words.Skip(1))}." : "The result.";
    }

    private static bool IsPlainTask(TypeSyntax type)
        => type is IdentifierNameSyntax { Identifier.Text: "Task" or "ValueTask" }
            || type is QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier.Text: "Task" or "ValueTask" } };

    private static string TypeReference(SyntaxNode member)
    {
        var type = member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is null)
        {
            return "type";
        }

        var generic = type.TypeParameterList is { Parameters.Count: > 0 } list ? "{" + string.Join(", ", list.Parameters.Select(p => p.Identifier.Text)) + "}" : string.Empty;
        return $"<see cref=\"{type.Identifier.Text}{generic}\"/>";
    }

    private static string TypeKindWord(SyntaxNode member) => member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() switch
    {
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "struct",
        RecordDeclarationSyntax => "record",
        _ => "class",
    };

    private static string ThirdPerson(string verb)
    {
        if (verb.EndsWith('y') && verb.Length > 1 && !"aeiou".Contains(verb[^2], StringComparison.Ordinal))
        {
            return verb[..^1] + "ies";
        }

        return verb.EndsWith('s') || verb.EndsWith('x') || verb.EndsWith('z') || verb.EndsWith("ch", StringComparison.Ordinal) || verb.EndsWith("sh", StringComparison.Ordinal)
            ? verb + "es"
            : verb + "s";
    }

    private static bool IsPredicate(string[] words) => words.Length > 1 && PredicateWords.Contains(words[0]);

    private static string StripPredicate(string words)
    {
        var parts = words.Split(' ');
        return IsPredicate(parts) ? string.Join(' ', parts.Skip(1)) : words;
    }

    private static string TrimAsync(string name)
        => name.Length > 5 && name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;

    private static string TrimInterfacePrefix(string name)
        => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;

    private static string Article(string words)
        => words.Length > 0 && "aeiou".Contains(words[0], StringComparison.Ordinal) ? "an " + words : "a " + words;
}
