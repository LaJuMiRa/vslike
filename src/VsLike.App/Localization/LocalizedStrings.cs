using System.ComponentModel;
using VsLike.Core.Localization;

namespace VsLike.App.Localization;

/// <summary>
/// Bindbare Quelle für UI-Texte. Benachrichtigt alle Bindungen, wenn die Sprache wechselt.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private LocalizedStrings()
    {
        Loc.CultureChanged += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static LocalizedStrings Instance { get; } = new();

    public string this[string key] => Loc.Get(key);
}
