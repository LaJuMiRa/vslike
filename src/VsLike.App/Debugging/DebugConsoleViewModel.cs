namespace VsLike.App.Debugging;

/// <summary>Inhalt der Debug-Konsole (Programmausgabe, Auswertung von Ausdrücken).</summary>
public sealed class DebugConsoleViewModel(DebugController controller)
{
    public DebugController Controller { get; } = controller;
}
