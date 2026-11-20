namespace VsLike.Core.Commands;

/// <summary>
/// Ein ausführbarer IDE-Befehl. Titel und Kategorie sind Ressourcenschlüssel (siehe <see cref="Localization.Loc"/>).
/// </summary>
public sealed record CommandDefinition
{
    public required string Id { get; init; }

    public required string TitleKey { get; init; }

    public string CategoryKey { get; init; } = "Category.General";

    public KeyChord? DefaultChord { get; init; }

    public required Func<Task> Execute { get; init; }

    public Func<bool>? CanExecute { get; init; }

    /// <summary>Gets a value indicating whether the command is listed in the command palette.</summary>
    public bool ShowInPalette { get; init; } = true;
}
