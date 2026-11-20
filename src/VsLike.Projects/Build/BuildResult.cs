using VsLike.Core.Build;
using VsLike.Core.Code;

namespace VsLike.Projects.Build;

public sealed record BuildResult(ProcessResult Process, IReadOnlyList<DiagnosticInfo> Diagnostics, TimeSpan Duration)
{
    public bool Succeeded => Process.Succeeded;

    public bool WasCancelled => Process.WasCancelled;

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverityKind.Error);

    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverityKind.Warning);
}
