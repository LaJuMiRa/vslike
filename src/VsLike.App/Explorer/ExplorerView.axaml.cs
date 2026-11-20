using Avalonia.Controls;
using Avalonia.Input;

namespace VsLike.App.Explorer;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        Tree.DoubleTapped += async (_, _) => await OpenSelectedAsync().ConfigureAwait(true);
        Tree.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSelectedAsync().ConfigureAwait(true);
            }
        };
    }

    private Task OpenSelectedAsync()
        => DataContext is ExplorerViewModel vm && Tree.SelectedItem is FileNodeViewModel node ? vm.OpenAsync(node) : Task.CompletedTask;
}
