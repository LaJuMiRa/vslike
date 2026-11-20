using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace VsLike.App.Search;

public partial class FindInFilesView : UserControl
{
    public FindInFilesView()
    {
        InitializeComponent();
        QueryBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is FindInFilesViewModel vm)
            {
                e.Handled = true;
                await vm.SearchAsync().ConfigureAwait(true);
            }
        };
        ResultTree.DoubleTapped += async (_, _) =>
        {
            if (DataContext is FindInFilesViewModel vm && ResultTree.SelectedItem is SearchResultLine line)
            {
                await vm.OpenAsync(line).ConfigureAwait(true);
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FindInFilesViewModel vm)
            {
                vm.FocusRequested += (_, _) => Dispatcher.UIThread.Post(() => QueryBox.Focus(), DispatcherPriority.Input);
            }
        };
    }
}
