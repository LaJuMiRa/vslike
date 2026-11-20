using System.Text.Json.Serialization;

namespace VsLike.Diagram.Model;

/// <summary>
/// Ein Typ im Diagramm. <see cref="FullName"/> (Namespace + verschachtelte Namen + <c>`Arity</c>) ist der stabile
/// Schlüssel für Layout und Änderungen. Externe Typen (nicht im Quelltext der Solution) sind schreibgeschützt.
/// </summary>
public sealed record DiagramType(
    string FullName,
    string Name,
    string Namespace,
    DiagramTypeKind Kind,
    Access Access = Access.Public,
    TypeModifiers Modifiers = TypeModifiers.None,
    IReadOnlyList<DiagramTypeParameter>? TypeParameters = null,
    string? BaseType = null,
    IReadOnlyList<string>? Interfaces = null,
    IReadOnlyList<DiagramMember>? Members = null,
    IReadOnlyList<string>? FilePaths = null,
    bool IsExternal = false)
{
    [JsonIgnore]
    public IReadOnlyList<DiagramTypeParameter> TypeParameterList => TypeParameters ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> InterfaceList => Interfaces ?? [];

    [JsonIgnore]
    public IReadOnlyList<DiagramMember> MemberList => Members ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> FileList => FilePaths ?? [];

    /// <summary>Gets the display name including type parameters, e.g. <c>Repository&lt;T&gt;</c>.</summary>
    public string DisplayName => TypeParameterList.Count == 0 ? Name : $"{Name}<{string.Join(", ", TypeParameterList.Select(t => t.Name))}>";
}
