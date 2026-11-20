using Avalonia.Controls;
using Avalonia.Input;

namespace VsLike.App.Problems;

public partial class ProblemsView : UserControl
{
    public ProblemsView()
    {
        InitializeComponent();
        List.DoubleTapped += async (_, _) => await OpenSelectedAsync().ConfigureAwait(true);
        List.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSelectedAsync().ConfigureAwait(true);
            }
        };
    }

    private Task OpenSelectedAsync()
        => DataContext is ProblemsViewModel vm && List.SelectedItem is ProblemItem item ? vm.OpenAsync(item) : Task.CompletedTask;
}
