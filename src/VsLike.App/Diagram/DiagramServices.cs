using Microsoft.CodeAnalysis;
using VsLike.App.Intelligence;
using VsLike.Core.Code;
using VsLike.Core.Documents;

namespace VsLike.App.Diagram;

/// <summary>Was ein Diagramm-Tab von der IDE braucht (Solution-Snapshot, Dateiänderungen, Dialoge, Navigation).</summary>
public sealed record DiagramServices(
    Func<Solution?> Solution,
    Func<IReadOnlyList<WorkspaceProject>> Projects,
    ILanguageFeatures Features,
    WorkspaceEditApplier Applier,
    IDialogService Dialogs,
    IDocumentNavigator Navigator,
    Func<string, string?> OpenText,
    Func<string, Task> DeleteFileAsync);
