using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VsLike.App.Dialogs;

/// <summary>Kleiner modaler Dialog mit Text und frei wählbaren Schaltflächen.</summary>
public sealed class ChoiceDialog : Window
{
    private ChoiceDialog(string title, string message, IReadOnlyList<(string Label, object Result, bool IsDefault)> buttons)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, result, isDefault) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => Close(result);
            buttonPanel.Children.Add(button);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15 },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttonPanel,
            },
        };
    }

    public static Task<T?> ShowAsync<T>(Window owner, string title, string message, params (string Label, T Result, bool IsDefault)[] buttons)
    {
        var dialog = new ChoiceDialog(title, message, [.. buttons.Select(b => (b.Label, (object)b.Result!, b.IsDefault))]);
        return dialog.ShowDialog<T?>(owner);
    }
}
