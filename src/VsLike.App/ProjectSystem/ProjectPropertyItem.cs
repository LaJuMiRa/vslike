using CommunityToolkit.Mvvm.ComponentModel;

namespace VsLike.App.ProjectSystem;

/// <summary>Eine bearbeitbare csproj-Eigenschaft; <see cref="Choices"/> leer = Freitext.</summary>
public sealed partial class ProjectPropertyItem : ObservableObject
{
    [ObservableProperty]
    private string _value;

    public ProjectPropertyItem(string name, string? value, IReadOnlyList<string> choices)
    {
        Name = name;
        _value = value ?? string.Empty;
        OriginalValue = _value;
        Choices = choices;
    }

    public string Name { get; }

    public string OriginalValue { get; private set; }

    public IReadOnlyList<string> Choices { get; }

    public bool HasChoices => Choices.Count > 0;

    public bool IsChanged => Value != OriginalValue;

    public void AcceptChanges() => OriginalValue = Value;
}
