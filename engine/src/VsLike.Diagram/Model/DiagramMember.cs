using System.Text.Json.Serialization;

namespace VsLike.Diagram.Model;

/// <summary>
/// Ein Mitglied eines Typs. <see cref="Type"/> ist der Rückgabe-/Feld-/Property-Typ wie im Quelltext geschrieben
/// (bei Konstruktoren und Enum-Werten leer), <see cref="Value"/> der Wert eines Enum-Mitglieds bzw. Initialisierer.
/// </summary>
public sealed record DiagramMember(
    MemberKind Kind,
    string Name,
    string Type = "",
    Access Access = Access.Public,
    MemberModifiers Modifiers = MemberModifiers.None,
    PropertyAccessors Accessors = PropertyAccessors.GetSet,
    IReadOnlyList<DiagramParameter>? Parameters = null,
    string? Value = null)
{
    [JsonIgnore]
    public IReadOnlyList<DiagramParameter> ParameterList => Parameters ?? [];

    /// <summary>Gets a key that identifies the member within its type (name plus parameter types for methods).</summary>
    public string Signature => Kind is MemberKind.Method or MemberKind.Constructor
        ? $"{Name}({string.Join(",", ParameterList.Select(p => p.Type))})"
        : Name;
}
