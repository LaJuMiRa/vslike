namespace VsLike.Core.Build;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<OutputLine> Output, bool WasCancelled)
{
    public bool Succeeded => ExitCode == 0 && !WasCancelled;

    public string StandardOutput => string.Join('\n', Output.Where(l => !l.IsError).Select(l => l.Text));
}
