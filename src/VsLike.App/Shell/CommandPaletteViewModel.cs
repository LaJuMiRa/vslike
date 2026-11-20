using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Core.Commands;
using VsLike.Core.Localization;
using VsLike.Core.Platform;

namespace VsLike.App.Shell;

/// <summary>Befehlspalette: Fuzzy-Suche über alle registrierten Befehle.</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int MaxResults = 50;
    private readonly ICommandRegistry _commands;
    private readonly PlatformKind _platform;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteItem? _selectedItem;

    public CommandPaletteViewModel(ICommandRegistry commands, PlatformKind platform)
    {
        _commands = commands;
        _platform = platform;
    }

    public ObservableCollection<PaletteItem> Items { get; } = [];

    public bool HasNoResults => IsOpen && Items.Count == 0;

    public void Open()
    {
        Query = string.Empty;
        Refresh();
        IsOpen = true;
        OnPropertyChanged(nameof(HasNoResults));
    }

    public void Close() => IsOpen = false;

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        SelectedItem = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    /// <summary>Executes the selected command and closes the palette.</summary>
    public async Task<bool> ExecuteSelectedAsync()
    {
        var item = SelectedItem;
        Close();
        return item is not null && await _commands.ExecuteAsync(item.CommandId).ConfigureAwait(true);
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        var ranked = _commands.Commands
            .Where(c => c.ShowInPalette && (c.CanExecute?.Invoke() ?? true))
            .Select(c => (Command: c, Title: Loc.Get(c.TitleKey), Category: Loc.Get(c.CategoryKey)))
            .Select(x => (x.Command, x.Title, x.Category, Score: FuzzyMatcher.Score(Query, $"{x.Category}: {x.Title}")))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title, StringComparer.CurrentCulture)
            .Take(MaxResults)
            .Select(x => new PaletteItem(x.Command.Id, x.Title, x.Category, _commands.GetChord(x.Command.Id)?.ToDisplayString(_platform) ?? string.Empty))
            .ToList();

        Items.Clear();
        foreach (var item in ranked)
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoResults));
    }
}
