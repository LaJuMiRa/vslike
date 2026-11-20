using CommunityToolkit.Mvvm.ComponentModel;

namespace VsLike.App.Debugging;

public sealed partial class WatchItem : ObservableObject
{
    [ObservableProperty]
    private string _value = string.Empty;

    public WatchItem(string expression)
    {
        Expression = expression;
    }

    public string Expression { get; }
}
