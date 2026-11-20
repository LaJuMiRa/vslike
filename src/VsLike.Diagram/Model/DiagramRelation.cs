namespace VsLike.Diagram.Model;

/// <param name="Label">Member that creates an association (field/property name), otherwise null.</param>
/// <param name="IsMany">True for collection-typed associations (multiplicity *).</param>
public sealed record DiagramRelation(string From, string To, RelationKind Kind, string? Label = null, bool IsMany = false);
