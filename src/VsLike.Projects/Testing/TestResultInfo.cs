namespace VsLike.Projects.Testing;

/// <param name="Name">Test name as reported by the runner, e.g. <c>Ns.Class.Method(x: 1)</c>.</param>
public sealed record TestResultInfo(string Name, TestOutcome Outcome, TimeSpan Duration, string? Message, string? StackTrace);
