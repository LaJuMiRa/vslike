using VsLike.Diagram.Model;

namespace VsLike.Diagram.Code;

/// <summary>Erzeugt C#-Quelltext für neue Typen und Mitglieder (mit <c>\n</c>, ohne Einrückung).</summary>
public static class CodeText
{
    public static string Keyword(Access access) => access switch
    {
        Access.Public => "public",
        Access.Internal => "internal",
        Access.Protected => "protected",
        Access.ProtectedInternal => "protected internal",
        Access.PrivateProtected => "private protected",
        _ => "private",
    };

    public static string Keyword(DiagramTypeKind kind) => kind switch
    {
        DiagramTypeKind.Interface => "interface",
        DiagramTypeKind.Struct => "struct",
        DiagramTypeKind.Enum => "enum",
        DiagramTypeKind.Record => "record",
        DiagramTypeKind.RecordStruct => "record struct",
        _ => "class",
    };

    public static string TypeModifiers(Access access, TypeModifiers modifiers)
    {
        var parts = new List<string> { Keyword(access) };
        if (modifiers.HasFlag(Model.TypeModifiers.Static))
        {
            parts.Add("static");
        }

        if (modifiers.HasFlag(Model.TypeModifiers.Abstract))
        {
            parts.Add("abstract");
        }

        if (modifiers.HasFlag(Model.TypeModifiers.Sealed))
        {
            parts.Add("sealed");
        }

        if (modifiers.HasFlag(Model.TypeModifiers.Partial))
        {
            parts.Add("partial");
        }

        return string.Join(' ', parts);
    }

    /// <summary>The header line(s) of a type: modifiers, keyword, name, type parameters, base list and constraints.</summary>
    public static string TypeHeader(DiagramType type, string newLine, string indentUnit)
    {
        var header = $"{TypeModifiers(type.Access, type.Modifiers)} {Keyword(type.Kind)} {type.DisplayName}";
        var bases = BaseList(type.BaseType, type.InterfaceList);
        if (bases.Length > 0)
        {
            header += " : " + bases;
        }

        foreach (var parameter in type.TypeParameterList.Where(p => p.Constraints.Length > 0))
        {
            header += $"{newLine}{indentUnit}where {parameter.Name} : {parameter.Constraints}";
        }

        return header;
    }

    public static string BaseList(string? baseType, IReadOnlyList<string> interfaces)
        => string.Join(", ", new[] { baseType }.Concat(interfaces).Where(b => !string.IsNullOrWhiteSpace(b)));

    /// <summary>A complete file with a block-scoped namespace (<c>namespace X { … }</c>) and one empty type.</summary>
    public static string TypeFile(DiagramType type, string? fileNamespace)
    {
        if (string.IsNullOrEmpty(fileNamespace))
        {
            return $"{TypeHeader(type, "\n", "    ")}\n{{\n}}\n";
        }

        return $"namespace {fileNamespace}\n{{\n    {TypeHeader(type, "\n    ", "    ")}\n    {{\n    }}\n}}\n";
    }

    public static string Member(DiagramMember member, DiagramType owner)
    {
        var isInterface = owner.Kind == DiagramTypeKind.Interface;
        var access = isInterface && member.Access == Access.Public ? string.Empty : Keyword(member.Access) + " ";
        var parameters = string.Join(", ", member.ParameterList.Select(p => $"{p.Type} {p.Name}"));
        var initializer = string.IsNullOrEmpty(member.Value) ? string.Empty : " = " + member.Value;
        switch (member.Kind)
        {
            case MemberKind.Field:
                return $"{access}{Modifiers(member.Modifiers, MemberModifiers.Static | MemberModifiers.ReadOnly | MemberModifiers.Const)}{member.Type} {member.Name}{initializer};";
            case MemberKind.Property:
                var accessors = member.Accessors switch
                {
                    PropertyAccessors.GetInit => "{ get; init; }",
                    PropertyAccessors.GetOnly => "{ get; }",
                    PropertyAccessors.GetPrivateSet => isInterface ? "{ get; }" : "{ get; private set; }",
                    _ => "{ get; set; }",
                };
                var semicolon = initializer.Length > 0 ? initializer + ";" : string.Empty;
                return $"{access}{Modifiers(member.Modifiers, MemberModifiers.Static | MemberModifiers.Required | MemberModifiers.Abstract | MemberModifiers.Virtual | MemberModifiers.Override)}{member.Type} {member.Name} {accessors}{semicolon}";
            case MemberKind.Event:
                return $"{access}{Modifiers(member.Modifiers, MemberModifiers.Static | MemberModifiers.Abstract | MemberModifiers.Virtual | MemberModifiers.Override)}event {member.Type} {member.Name};";
            case MemberKind.Constructor:
                return $"{access}{Modifiers(member.Modifiers, MemberModifiers.Static)}{owner.Name}({parameters})\n{{\n}}";
            case MemberKind.EnumValue:
                return member.Name + initializer;
            default:
                var signature = $"{access}{Modifiers(member.Modifiers, MemberModifiers.Static | MemberModifiers.Abstract | MemberModifiers.Virtual | MemberModifiers.Override | MemberModifiers.Async)}{(string.IsNullOrEmpty(member.Type) ? "void" : member.Type)} {member.Name}({parameters})";
                return isInterface || member.Modifiers.HasFlag(MemberModifiers.Abstract)
                    ? signature + ";"
                    : $"{signature}\n{{\n    throw new NotImplementedException();\n}}";
        }
    }

    private static string Modifiers(MemberModifiers modifiers, MemberModifiers allowed)
    {
        var order = new (MemberModifiers Flag, string Text)[]
        {
            (MemberModifiers.Const, "const"),
            (MemberModifiers.Static, "static"),
            (MemberModifiers.Required, "required"),
            (MemberModifiers.Abstract, "abstract"),
            (MemberModifiers.Virtual, "virtual"),
            (MemberModifiers.Override, "override"),
            (MemberModifiers.ReadOnly, "readonly"),
            (MemberModifiers.Async, "async"),
        };
        return string.Concat(order.Where(o => (modifiers & allowed).HasFlag(o.Flag)).Select(o => o.Text + " "));
    }
}
