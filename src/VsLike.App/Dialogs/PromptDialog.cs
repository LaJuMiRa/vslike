using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using VsLike.Core.Localization;

namespace VsLike.App.Dialogs;

/// <summary>Modaler Eingabedialog (z. B. für Rename).</summary>
public sealed class PromptDialog : Window
{
    private PromptDialog(string title, string message, string initialValue)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var input = new TextBox { Text = initialValue };
        var ok = new Button { Content = Loc.Get("Dialog.Ok"), IsDefault = true, MinWidth = 88, Classes = { "accent" } };
        var cancel = new Button { Content = Loc.Get("Dialog.Cancel"), IsCancel = true, MinWidth = 88 };
        ok.Click += (_, _) => Close(input.Text);
        cancel.Click += (_, _) => Close(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
        Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15 },
                new TextBlock { Text = message },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
    }

    public static Task<string?> ShowAsync(Window owner, string title, string message, string initialValue)
        => new PromptDialog(title, message, initialValue).ShowDialog<string?>(owner);
}
