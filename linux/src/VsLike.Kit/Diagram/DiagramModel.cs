using System.Text.Json.Serialization;

namespace VsLike.Kit;

/* The class diagram model as the engine reads it from the code (engine/src/VsLike.Diagram/Model). */

public enum Access
{
    Public,
    Internal,
    Protected,
    ProtectedInternal,
    PrivateProtected,
    Private,
}

public enum DiagramTypeKind
{
    Class,
    Interface,
    Struct,
    Enum,
    Record,
    RecordStruct,
}

[Flags]
public enum TypeModifiers
{
    None = 0,
    Abstract = 1,
    Static = 2,
    Sealed = 4,
    Partial = 8,
}

[Flags]
public enum MemberModifiers
{
    None = 0,
    Static = 1,
    Abstract = 2,
    Virtual = 4,
    Override = 8,
    ReadOnly = 16,
    Const = 32,
    Async = 64,
    Required = 128,
}

public enum MemberKind
{
    Field,
    Property,
    Method,
    Constructor,
    Event,
    EnumValue,
}

public enum PropertyAccessors
{
    GetSet,
    GetInit,
    GetOnly,
    GetPrivateSet,
}

public enum RelationKind
{
    Inheritance,
    Implementation,
    Association,
    Composition,
}

public static class DiagramModelExtensions
{
    public static string Keyword(this Access access) => access switch
    {
        Access.Public => "public",
        Access.Internal => "internal",
        Access.Protected => "protected",
        Access.ProtectedInternal => "protected internal",
        Access.PrivateProtected => "private protected",
        _ => "private",
    };

    /// <summary>UML symbol: <c>+</c> public, <c>#</c> protected, <c>~</c> internal, <c>-</c> private.</summary>
    public static string Symbol(this Access access) => access switch
    {
        Access.Public => "+",
        Access.Protected or Access.ProtectedInternal or Access.PrivateProtected => "#",
        Access.Internal => "~",
        _ => "-",
    };

    /// <summary>The JSON/raw name (<c>protectedInternal</c>), as in the Swift raw values.</summary>
    public static string RawValue<T>(this T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string Keyword(this DiagramTypeKind kind) => kind == DiagramTypeKind.RecordStruct ? "record struct" : kind.RawValue();
}

public sealed record DiagramParameter(string Name, string Type);

/// <param name="Constraints">Constraint list as written after <c>where T :</c>, e.g. "class, new()"; empty if none.</param>
public sealed record DiagramTypeParameter(string Name, string Constraints = "");

public sealed record DiagramMember
{
    [JsonConstructor]
    public DiagramMember(
        MemberKind kind,
        string name,
        string? type = "",
        Access access = Access.Public,
        MemberModifiers modifiers = MemberModifiers.None,
        PropertyAccessors accessors = PropertyAccessors.GetSet,
        IReadOnlyList<DiagramParameter>? parameters = null,
        string? value = null)
    {
        Kind = kind;
        Name = name;
        Type = type ?? string.Empty;
        Access = access;
        Modifiers = modifiers;
        Accessors = accessors;
        Parameters = parameters ?? [];
        Value = value;
    }

    public MemberKind Kind { get; init; }

    public string Name { get; init; }

    public string Type { get; init; }

    public Access Access { get; init; }

    public MemberModifiers Modifiers { get; init; }

    public PropertyAccessors Accessors { get; init; }

    public IReadOnlyList<DiagramParameter> Parameters { get; init; }

    public string? Value { get; init; }

    /// <summary>Gets the key of the member within its type: the name, plus parameter types for methods and constructors.</summary>
    [JsonIgnore]
    public string Signature => Kind is MemberKind.Method or MemberKind.Constructor
        ? $"{Name}({string.Join(",", Parameters.Select(p => p.Type))})"
        : Name;

    /// <summary>Gets one UML-style line, e.g. <c>+ Total(withTax: bool): decimal</c>.</summary>
    [JsonIgnore]
    public string UmlLine
    {
        get
        {
            var parameterList = string.Join(", ", Parameters.Select(p => $"{p.Name}: {p.Type}"));
            return Kind switch
            {
                MemberKind.EnumValue => Value is null ? Name : $"{Name} = {Value}",
                MemberKind.Method => $"{Access.Symbol()} {Name}({parameterList}): {Type}",
                MemberKind.Constructor => $"{Access.Symbol()} {Name}({parameterList})",
                MemberKind.Event => $"{Access.Symbol()} «event» {Name}: {Type}",
                _ => $"{Access.Symbol()} {Name}: {Type}",
            };
        }
    }

    /// <summary>Gets the text the member editor starts with (same notation the engine parses back).</summary>
    [JsonIgnore]
    public string EditText => Kind != MemberKind.EnumValue && !string.IsNullOrEmpty(Value) ? UmlLine + " = " + Value : UmlLine;

    public bool Equals(DiagramMember? other) =>
        other is not null && Kind == other.Kind && Name == other.Name && Type == other.Type && Access == other.Access
        && Modifiers == other.Modifiers && Accessors == other.Accessors && Value == other.Value && Parameters.SequenceEqual(other.Parameters);

    public override int GetHashCode() => HashCode.Combine(Kind, Name, Type, Access, Modifiers, Accessors, Value, Parameters.Count);
}

public sealed record DiagramType
{
    [JsonConstructor]
    public DiagramType(
        string fullName,
        string name,
        string? @namespace = "",
        DiagramTypeKind kind = DiagramTypeKind.Class,
        Access access = Access.Public,
        TypeModifiers modifiers = TypeModifiers.None,
        IReadOnlyList<DiagramTypeParameter>? typeParameters = null,
        string? baseType = null,
        IReadOnlyList<string>? interfaces = null,
        IReadOnlyList<DiagramMember>? members = null,
        IReadOnlyList<string>? filePaths = null,
        bool isExternal = false)
    {
        FullName = fullName;
        Name = name;
        Namespace = @namespace ?? string.Empty;
        Kind = kind;
        Access = access;
        Modifiers = modifiers;
        TypeParameters = typeParameters ?? [];
        BaseType = baseType;
        Interfaces = interfaces ?? [];
        Members = members ?? [];
        FilePaths = filePaths ?? [];
        IsExternal = isExternal;
    }

    public string FullName { get; init; }

    public string Name { get; init; }

    public string Namespace { get; init; }

    public DiagramTypeKind Kind { get; init; }

    public Access Access { get; init; }

    public TypeModifiers Modifiers { get; init; }

    public IReadOnlyList<DiagramTypeParameter> TypeParameters { get; init; }

    public string? BaseType { get; init; }

    public IReadOnlyList<string> Interfaces { get; init; }

    public IReadOnlyList<DiagramMember> Members { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; }

    public bool IsExternal { get; init; }

    [JsonIgnore]
    public string Id => FullName;

    /// <summary>Gets the name with type parameters, e.g. <c>Repository&lt;T&gt;</c>.</summary>
    [JsonIgnore]
    public string DisplayName => TypeParameters.Count == 0 ? Name : $"{Name}<{string.Join(", ", TypeParameters.Select(t => t.Name))}>";

    /// <summary>Gets <c>«interface»</c>, <c>«abstract»</c> … or an empty string for a plain class.</summary>
    [JsonIgnore]
    public string Stereotype => Kind switch
    {
        DiagramTypeKind.Interface => "«interface»",
        DiagramTypeKind.Enum => "«enum»",
        DiagramTypeKind.Struct => "«struct»",
        DiagramTypeKind.Record => "«record»",
        DiagramTypeKind.RecordStruct => "«record struct»",
        _ when Modifiers.HasFlag(TypeModifiers.Static) => "«static»",
        _ when Modifiers.HasFlag(TypeModifiers.Abstract) => "«abstract»",
        _ => IsExternal ? "«extern»" : string.Empty,
    };

    public bool Equals(DiagramType? other) =>
        other is not null && FullName == other.FullName && Name == other.Name && Namespace == other.Namespace && Kind == other.Kind
        && Access == other.Access && Modifiers == other.Modifiers && BaseType == other.BaseType && IsExternal == other.IsExternal
        && TypeParameters.SequenceEqual(other.TypeParameters) && Interfaces.SequenceEqual(other.Interfaces)
        && Members.SequenceEqual(other.Members) && FilePaths.SequenceEqual(other.FilePaths);

    public override int GetHashCode() => HashCode.Combine(FullName, Name, Kind, Members.Count);
}

public sealed record DiagramRelation(string From, string To, RelationKind Kind, string? Label = null, bool IsMany = false);

public sealed record DiagramModel(IReadOnlyList<DiagramType> Types, IReadOnlyList<DiagramRelation> Relations)
{
    public static readonly DiagramModel Empty = new([], []);

    public DiagramType? Find(string fullName) => Types.FirstOrDefault(t => t.FullName == fullName);

    public bool Equals(DiagramModel? other) =>
        other is not null && Types.SequenceEqual(other.Types) && Relations.SequenceEqual(other.Relations);

    public override int GetHashCode() => HashCode.Combine(Types.Count, Relations.Count);
}
