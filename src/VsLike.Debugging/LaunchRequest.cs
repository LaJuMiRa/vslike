namespace VsLike.Debugging;

/// <summary>What to start: the built assembly, arguments, working folder and environment.</summary>
public sealed record LaunchRequest(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment, bool StopAtEntry = false);
