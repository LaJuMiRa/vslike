namespace VsLike.Core.Commands;

/// <summary>
/// Beschreibt die Hauptmenüs. Jedes Feature hängt seine Befehle selbst ein; Gruppen werden durch Trennlinien getrennt.
/// </summary>
public sealed class MenuRegistry
{
    public static readonly IReadOnlyList<string> TopLevelMenus = ["Menu.File", "Menu.Edit", "Menu.View", "Menu.Build", "Menu.Debug", "Menu.Window", "Menu.Help"];

    private readonly List<MenuEntry> _entries = [];

    public event EventHandler? Changed;

    public void Add(string menuKey, string commandId, int group = 0)
    {
        _entries.Add(new MenuEntry(menuKey, commandId, group, _entries.Count));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the entries of a menu ordered by group and insertion order.</summary>
    public IReadOnlyList<MenuEntry> GetEntries(string menuKey)
        => [.. _entries.Where(e => e.MenuKey == menuKey).OrderBy(e => e.Group).ThenBy(e => e.Sequence)];

    public bool Contains(string commandId) => _entries.Any(e => e.CommandId == commandId);
}
