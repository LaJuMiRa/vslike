using VsLike.Core.Commands;

namespace VsLike.Core.Tests;

public class CommandRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_RunsRegisteredCommand()
    {
        var registry = new CommandRegistry();
        var runs = 0;
        registry.Register(Create("test.run", () => runs++));

        Assert.True(await registry.ExecuteAsync("test.run"));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsWhenCanExecuteIsFalse()
    {
        var registry = new CommandRegistry();
        var runs = 0;
        registry.Register(Create("test.run", () => runs++) with { CanExecute = () => false });

        Assert.False(await registry.ExecuteAsync("test.run"));
        Assert.False(await registry.ExecuteAsync("missing"));
        Assert.Equal(0, runs);
    }

    [Fact]
    public void Register_RejectsDuplicateIds()
    {
        var registry = new CommandRegistry();
        registry.Register(Create("a", () => { }));

        Assert.Throws<InvalidOperationException>(() => registry.Register(Create("a", () => { })));
    }

    [Fact]
    public void GetChord_PrefersUserOverride()
    {
        var registry = new CommandRegistry();
        registry.Register(Create("a", () => { }) with { DefaultChord = KeyChord.Parse("Cmd+A") });
        registry.Register(Create("b", () => { }) with { DefaultChord = KeyChord.Parse("Cmd+B") });

        registry.SetOverrides(new Dictionary<string, string> { ["a"] = "Cmd+Shift+A", ["b"] = string.Empty });

        Assert.Equal(KeyChord.Parse("Cmd+Shift+A"), registry.GetChord("a"));
        Assert.Null(registry.GetChord("b"));
    }

    private static CommandDefinition Create(string id, Action action) => new()
    {
        Id = id,
        TitleKey = id,
        Execute = () =>
        {
            action();
            return Task.CompletedTask;
        },
    };
}
