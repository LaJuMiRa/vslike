using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Code;

/// <summary>
/// Schreibt Diagrammänderungen in den Quelltext. Roslyn findet den betroffenen Knoten; ersetzt wird nur dessen
/// Textbereich, sodass Formatierung, Kommentare und der Rest der Datei unverändert bleiben. Zeilenende und
/// Einrückung werden aus der Datei übernommen.
/// </summary>
public static class DiagramWriter
{
    public static string AddMember(string source, string typeFullName, DiagramMember member)
    {
        var layout = TextLayout.Detect(source);
        var type = SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        if (type is EnumDeclarationSyntax enumeration)
        {
            return AddEnumValue(source, enumeration, member, layout);
        }

        var declaration = (TypeDeclarationSyntax)type;
        if (declaration.OpenBraceToken.IsKind(SyntaxKind.None) || declaration.OpenBraceToken.IsMissing)
        {
            source = ExpandBody(source, declaration, layout);
            declaration = (TypeDeclarationSyntax)SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        }

        var typeIndent = TextLayout.IndentOfLine(source, declaration.Keyword.SpanStart);
        if (SameLine(source, declaration.OpenBraceToken.SpanStart, declaration.CloseBraceToken.SpanStart))
        {
            source = source[..declaration.OpenBraceToken.SpanStart].TrimEnd(' ', '\t')
                + layout.NewLine + typeIndent + "{" + layout.NewLine + typeIndent + "}"
                + source[declaration.CloseBraceToken.Span.End..];
            declaration = (TypeDeclarationSyntax)SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        }

        var memberIndent = declaration.Members.Count > 0 ? TextLayout.IndentOfLine(source, declaration.Members[0].SpanStart) : typeIndent + layout.IndentUnit;
        var code = layout.Indent(CodeText.Member(member, OwnerOf(declaration)), memberIndent);
        var rank = Rank(member.Kind);
        var anchor = declaration.Members.LastOrDefault(m => Rank(m) <= rank);
        if (anchor is null)
        {
            var position = TextLayout.NextLineStart(source, declaration.OpenBraceToken.Span.End);
            var separator = declaration.Members.Count > 0 ? layout.NewLine : string.Empty;
            return source.Insert(position, code + layout.NewLine + separator);
        }

        var blank = member.Kind == MemberKind.Field && anchor is FieldDeclarationSyntax ? string.Empty : layout.NewLine;
        var after = TextLayout.NextLineStart(source, anchor.Span.End);
        return source.Insert(after, blank + code + layout.NewLine);
    }

    public static string RemoveMember(string source, string typeFullName, string signature)
    {
        var type = SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        var member = SyntaxLocator.FindMember(type, signature);
        if (type is EnumDeclarationSyntax enumeration)
        {
            var updated = enumeration.WithMembers(enumeration.Members.Remove((EnumMemberDeclarationSyntax)member));
            return source[..enumeration.FullSpan.Start] + updated.ToFullString() + source[enumeration.FullSpan.End..];
        }

        var result = source.Remove(member.FullSpan.Start, member.FullSpan.Length);
        return RemoveBlankLineAfterOpenBrace(result, member.FullSpan.Start);
    }

    /// <summary>Changes access, modifiers, type, name or parameters of a member; bodies, accessors with code and attributes stay.</summary>
    public static string UpdateMember(string source, string typeFullName, string signature, DiagramMember updated)
    {
        var type = SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        var member = SyntaxLocator.FindMember(type, signature);
        var owner = OwnerOf(type);
        var full = CodeText.Member(updated, owner);
        switch (member)
        {
            case MethodDeclarationSyntax method when method.Body is not null || method.ExpressionBody is not null:
                return Replace(source, HeaderStart(method.Modifiers, method.ReturnType), method.ParameterList.Span.End, HeaderOf(full));
            case ConstructorDeclarationSyntax constructor:
                return Replace(source, HeaderStart(constructor.Modifiers, constructor.Identifier), constructor.ParameterList.Span.End, HeaderOf(full));
            case PropertyDeclarationSyntax property when property.AccessorList is { } accessors && accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null):
                return Replace(source, HeaderStart(property.Modifiers, property.Type), property.Identifier.Span.End, full[..full.IndexOf(" {", StringComparison.Ordinal)]);
            case EnumMemberDeclarationSyntax enumMember:
                return Replace(source, enumMember.Identifier.SpanStart, enumMember.Span.End, full);
            default:
                var start = member.AttributeLists.Count > 0 ? member.AttributeLists.Last().GetLastToken().GetNextToken().SpanStart : member.SpanStart;
                var layout = TextLayout.Detect(source);
                var indent = TextLayout.IndentOfLine(source, member.SpanStart);
                var text = layout.Indent(full, indent).TrimStart();
                return Replace(source, start, member.Span.End, text);
        }
    }

    /// <summary>Sets base class and interfaces (<c>: Base, IFoo</c>); an empty list removes the base list.</summary>
    public static string SetBaseList(string source, string typeFullName, string? baseType, IReadOnlyList<string> interfaces)
    {
        var declaration = (TypeDeclarationSyntax)SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        var list = CodeText.BaseList(baseType, interfaces);
        if (declaration.BaseList is { } existing)
        {
            var start = existing.SpanStart;
            while (start > 0 && source[start - 1] == ' ')
            {
                start--;
            }

            return Replace(source, start, existing.Span.End, list.Length == 0 ? string.Empty : " : " + list);
        }

        if (list.Length == 0)
        {
            return source;
        }

        var anchorEnd = declaration.Identifier.Span.End;
        if (declaration.ParameterList is { } parameters)
        {
            anchorEnd = parameters.Span.End;
        }
        else if (declaration.TypeParameterList is { } typeParameters)
        {
            anchorEnd = typeParameters.Span.End;
        }

        return source.Insert(anchorEnd, " : " + list);
    }

    public static string SetTypeModifiers(string source, string typeFullName, Access access, TypeModifiers modifiers)
    {
        var type = SyntaxLocator.FindType(SyntaxLocator.Parse(source), typeFullName);
        var keyword = type switch
        {
            TypeDeclarationSyntax t => t.Keyword,
            EnumDeclarationSyntax e => e.EnumKeyword,
            _ => throw new InvalidOperationException(),
        };
        var start = type.Modifiers.Count > 0 ? type.Modifiers[0].SpanStart : keyword.SpanStart;
        return Replace(source, start, keyword.SpanStart, CodeText.TypeModifiers(access, modifiers) + " ");
    }

    /// <summary>Removes a type declaration; returns null when no type is left (the caller deletes the file).</summary>
    public static string? RemoveType(string source, string typeFullName)
    {
        var root = SyntaxLocator.Parse(source);
        var type = SyntaxLocator.FindType(root, typeFullName);
        if (SyntaxLocator.Types(root).Count(t => t.Parent is not BaseTypeDeclarationSyntax) == 1 && type.Parent is not BaseTypeDeclarationSyntax)
        {
            return null;
        }

        return RemoveBlankLineAfterOpenBrace(source.Remove(type.FullSpan.Start, type.FullSpan.Length), type.FullSpan.Start);
    }

    internal static DiagramType OwnerOf(BaseTypeDeclarationSyntax declaration)
    {
        var kind = declaration switch
        {
            InterfaceDeclarationSyntax => DiagramTypeKind.Interface,
            StructDeclarationSyntax => DiagramTypeKind.Struct,
            EnumDeclarationSyntax => DiagramTypeKind.Enum,
            RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => DiagramTypeKind.RecordStruct,
            RecordDeclarationSyntax => DiagramTypeKind.Record,
            _ => DiagramTypeKind.Class,
        };
        return new DiagramType(SyntaxLocator.FullNameOf(declaration), declaration.Identifier.Text, string.Empty, kind);
    }

    private static string AddEnumValue(string source, EnumDeclarationSyntax enumeration, DiagramMember member, TextLayout layout)
    {
        var code = CodeText.Member(member with { Kind = MemberKind.EnumValue }, OwnerOf(enumeration));
        if (enumeration.Members.Count == 0)
        {
            var indent = TextLayout.IndentOfLine(source, enumeration.EnumKeyword.SpanStart) + layout.IndentUnit;
            return source.Insert(TextLayout.NextLineStart(source, enumeration.OpenBraceToken.Span.End), indent + code + layout.NewLine);
        }

        var last = enumeration.Members.Last();
        var memberIndent = TextLayout.IndentOfLine(source, last.SpanStart);
        if (enumeration.Members.SeparatorCount == enumeration.Members.Count)
        {
            var position = TextLayout.NextLineStart(source, enumeration.Members.GetSeparator(enumeration.Members.SeparatorCount - 1).Span.End);
            return source.Insert(position, memberIndent + code + "," + layout.NewLine);
        }

        return source.Insert(last.Span.End, "," + layout.NewLine + memberIndent + code);
    }

    private static string ExpandBody(string source, TypeDeclarationSyntax declaration, TextLayout layout)
    {
        var indent = TextLayout.IndentOfLine(source, declaration.Keyword.SpanStart);
        return Replace(source, declaration.SemicolonToken.SpanStart, declaration.SemicolonToken.Span.End, layout.NewLine + indent + "{" + layout.NewLine + indent + "}");
    }

    private static int Rank(MemberKind kind) => kind switch
    {
        MemberKind.Field => 0,
        MemberKind.Constructor => 1,
        MemberKind.Event => 2,
        MemberKind.Property => 3,
        _ => 4,
    };

    private static int Rank(MemberDeclarationSyntax member) => member switch
    {
        FieldDeclarationSyntax => 0,
        ConstructorDeclarationSyntax or DestructorDeclarationSyntax => 1,
        EventFieldDeclarationSyntax or EventDeclarationSyntax => 2,
        PropertyDeclarationSyntax or IndexerDeclarationSyntax => 3,
        MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => 4,
        _ => 5,
    };

    private static int HeaderStart(SyntaxTokenList modifiers, SyntaxNodeOrToken first) => modifiers.Count > 0 ? modifiers[0].SpanStart : first.SpanStart;

    private static string HeaderOf(string code)
    {
        var end = code.IndexOf(')', StringComparison.Ordinal);
        return code[..(end + 1)];
    }

    private static string Replace(string source, int start, int end, string text) => source[..start] + text + source[end..];

    private static bool SameLine(string source, int a, int b) => source.IndexOf('\n', a, b - a) < 0;

    private static string RemoveBlankLineAfterOpenBrace(string source, int position)
    {
        var lineStart = TextLayout.LineStart(source, position);
        var before = source[..lineStart].TrimEnd(' ', '\t', '\r', '\n');
        if (!before.EndsWith('{'))
        {
            return source;
        }

        var rest = source[lineStart..];
        var trimmed = rest.TrimStart(' ', '\t');
        if (trimmed.StartsWith("\r\n", StringComparison.Ordinal))
        {
            return source[..lineStart] + trimmed[2..];
        }

        return trimmed.StartsWith('\n') ? source[..lineStart] + trimmed[1..] : source;
    }
}
