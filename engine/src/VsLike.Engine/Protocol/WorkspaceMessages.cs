namespace VsLike.Engine.Protocol;

public sealed record InitializeParams(string? Culture);

public sealed record InitializeResult(string EngineVersion, string MSBuildPath, string SdkVersion);

public sealed record PathParams(string Path);

public sealed record TextParams(string Path, string Text);

public sealed record OffsetParams(string Path, int Offset);

public sealed record RangeParams(string Path, int Start, int Length);

public sealed record StyleCopParams(bool Enabled);

public sealed record DiagnosticsNotification(string? Path, IReadOnlyList<Core.Code.DiagnosticInfo> Diagnostics);
