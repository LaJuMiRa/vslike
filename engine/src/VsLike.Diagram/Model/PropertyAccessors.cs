namespace VsLike.Diagram.Model;

/// <summary>Accessor shape of a property: <c>{ get; set; }</c>, <c>{ get; init; }</c>, <c>{ get; }</c>.</summary>
public enum PropertyAccessors
{
    GetSet,
    GetInit,
    GetOnly,
    GetPrivateSet,
}
