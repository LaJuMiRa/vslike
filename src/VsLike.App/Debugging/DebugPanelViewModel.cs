namespace VsLike.App.Debugging;

/// <summary>Inhalt des Debug-Panels (Variablen, Überwachen, Aufrufliste, Threads).</summary>
public sealed class DebugPanelViewModel(DebugController controller)
{
    public DebugController Controller { get; } = controller;
}
