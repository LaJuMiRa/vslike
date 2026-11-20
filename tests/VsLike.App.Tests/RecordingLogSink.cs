namespace VsLike.App.Tests;

/// <summary>Records Avalonia errors (layout/template failures are only logged, not thrown).</summary>
internal sealed class RecordingLogSink : Avalonia.Logging.ILogSink
{
    public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) => level >= Avalonia.Logging.LogEventLevel.Error || (level == Avalonia.Logging.LogEventLevel.Warning && area is "Layout" or "Visual" or "Control");

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        => UiWait.Record($"log:{level}:{area}", new InvalidOperationException($"{messageTemplate} (source {source?.GetType().Name})"));

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        => UiWait.Record($"log:{level}:{area}", new InvalidOperationException($"{messageTemplate} [{string.Join(", ", propertyValues)}] (source {source?.GetType().Name})"));
}
