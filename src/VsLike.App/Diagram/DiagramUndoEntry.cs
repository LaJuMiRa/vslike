using VsLike.Diagram.Layout;

namespace VsLike.App.Diagram;

/// <summary>Ein Schritt im gemeinsamen Undo von Diagramm und Code: Dateiinhalte und Layout davor und danach.</summary>
public sealed record DiagramUndoEntry(string Description, IReadOnlyList<FileChange> Files, DiagramDocument LayoutBefore, DiagramDocument LayoutAfter);
