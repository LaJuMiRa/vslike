namespace VsLike.Core.Commands;

public interface ICommandRegistry
{
    event EventHandler? Changed;

    IReadOnlyList<CommandDefinition> Commands { get; }

    void Register(CommandDefinition command);

    CommandDefinition? Find(string id);

    /// <summary>Returns the effective chord (user override or default).</summary>
    KeyChord? GetChord(string id);

    /// <summary>Replaces the user key binding overrides (command id → chord string, empty string = unbound).</summary>
    void SetOverrides(IReadOnlyDictionary<string, string> overrides);

    Task<bool> ExecuteAsync(string id);
}
