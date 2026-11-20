namespace VsLike.Core.Commands;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.Ordinal);
    private readonly List<CommandDefinition> _ordered = [];
    private IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>();

    public event EventHandler? Changed;

    public IReadOnlyList<CommandDefinition> Commands => _ordered;

    public void Register(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"Command '{command.Id}' is already registered.");
        }

        _ordered.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public CommandDefinition? Find(string id) => _commands.GetValueOrDefault(id);

    public KeyChord? GetChord(string id)
    {
        if (_overrides.TryGetValue(id, out var text))
        {
            return KeyChord.TryParse(text, out var chord) ? chord : null;
        }

        return Find(id)?.DefaultChord;
    }

    public void SetOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        _overrides = new Dictionary<string, string>(overrides, StringComparer.Ordinal);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ExecuteAsync(string id)
    {
        var command = Find(id);
        if (command is null || (command.CanExecute is not null && !command.CanExecute()))
        {
            return false;
        }

        await command.Execute().ConfigureAwait(true);
        return true;
    }
}
