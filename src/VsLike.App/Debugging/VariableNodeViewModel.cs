using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VsLike.App.Debugging;

/// <summary>Variable oder Scope im Variablenbaum; Kinder werden beim Aufklappen nachgeladen.</summary>
public sealed partial class VariableNodeViewModel : ObservableObject
{
    private readonly Func<int, Task<IReadOnlyList<VariableNodeViewModel>>>? _load;
    private bool _loaded;

    [ObservableProperty]
    private bool _isExpanded;

    public VariableNodeViewModel(string name, string value, string? type, int reference, Func<int, Task<IReadOnlyList<VariableNodeViewModel>>>? load)
    {
        Name = name;
        Value = value;
        Type = type;
        Reference = reference;
        _load = load;
        if (reference != 0)
        {
            Children.Add(new VariableNodeViewModel("…", string.Empty, null, 0, null));
        }
    }

    public string Name { get; }

    public string Value { get; }

    public string? Type { get; }

    public int Reference { get; }

    public ObservableCollection<VariableNodeViewModel> Children { get; } = [];

    public string Display => Value.Length == 0 ? Name : $"{Name} = {Value}";

    public async Task LoadChildrenAsync()
    {
        if (_loaded || _load is null || Reference == 0)
        {
            return;
        }

        _loaded = true;
        var children = await _load(Reference).ConfigureAwait(true);
        Children.Clear();
        foreach (var child in children)
        {
            Children.Add(child);
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }
}
