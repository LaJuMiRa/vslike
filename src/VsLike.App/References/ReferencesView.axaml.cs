using Avalonia.Controls;
using VsLike.App.Search;

namespace VsLike.App.References;

public partial class ReferencesView : UserControl
{
    public ReferencesView()
    {
        InitializeComponent();
        ResultTree.DoubleTapped += async (_, _) =>
        {
            if (DataContext is ReferencesViewModel vm && ResultTree.SelectedItem is SearchResultLine line)
            {
                await vm.OpenAsync(line).ConfigureAwait(true);
            }
        };
    }
}
