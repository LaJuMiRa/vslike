namespace VsLike.Diagram.Model;

/// <param name="Constraints">Constraint list as written after <c>where T :</c>, e.g. "class, new()"; empty if none.</param>
public sealed record DiagramTypeParameter(string Name, string Constraints = "");
