namespace VsLike.App.Shell;

/// <summary>Ein Bereich mit eigenem Rückgängig/Wiederholen (z. B. das Klassendiagramm), erreichbar über ⌘Z/⇧⌘Z.</summary>
public interface IEditTarget
{
    bool TryEdit(EditAction action);
}
