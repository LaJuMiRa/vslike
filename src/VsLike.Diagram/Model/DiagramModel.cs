namespace VsLike.Diagram.Model;

/// <summary>Unveränderlicher Stand aller Typen und Beziehungen, aus dem Code gelesen.</summary>
public sealed record DiagramModel(IReadOnlyList<DiagramType> Types, IReadOnlyList<DiagramRelation> Relations)
{
    public static readonly DiagramModel Empty = new([], []);

    public DiagramType? Find(string fullName) => Types.FirstOrDefault(t => t.FullName == fullName);
}
