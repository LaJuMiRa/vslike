using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VsLike.Core.Localization;

namespace VsLike.App.ProjectSystem;

/// <summary>Auswahl der Projektverweise (Checkboxen über alle anderen Projekte der Solution).</summary>
public sealed class ReferencesDialog : Window
{
    private ReferencesDialog(string project, IReadOnlyList<string> candidates, IReadOnlyCollection<string> current)
    {
        Title = Loc.Format("Reference.Title", Path.GetFileNameWithoutExtension(project));
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var boxes = candidates.Select(c => new CheckBox { Content = Path.GetFileNameWithoutExtension(c), Tag = c, IsChecked = current.Contains(c) }).ToList();
        var apply = new Button { Content = Loc.Get("Reference.Apply"), IsDefault = true, Classes = { "accent" }, MinWidth = 100 };
        var cancel = new Button { Content = Loc.Get("Dialog.Cancel"), IsCancel = true, MinWidth = 100 };
        apply.Click += (_, _) => Close(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList());
        cancel.Click += (_, _) => Close(null);
        var list = new StackPanel { Spacing = 6 };
        foreach (var box in boxes)
        {
            list.Children.Add(box);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = Title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15 },
                new ScrollViewer { MaxHeight = 360, Content = list },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } },
            },
        };
    }

    public static Task<List<string>?> ShowAsync(Window owner, string project, IReadOnlyList<string> candidates, IReadOnlyCollection<string> current)
        => new ReferencesDialog(project, candidates, current).ShowDialog<List<string>?>(owner);
}
