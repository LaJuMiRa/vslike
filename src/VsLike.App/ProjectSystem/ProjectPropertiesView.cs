using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using VsLike.Core.Localization;

namespace VsLike.App.ProjectSystem;

/// <summary>Ansicht des Eigenschaften-Formulars; Speichern ruft <see cref="Saved"/> auf (lädt die Solution neu).</summary>
public sealed class ProjectPropertiesView : UserControl
{
    public static Func<ProjectPropertiesViewModel, Task>? Saved { get; set; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ProjectPropertiesViewModel vm)
        {
            return;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), RowSpacing = 8, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        var row = 0;
        foreach (var property in vm.Properties)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = property.Name, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
            Control input = property.HasChoices
                ? new ComboBox { ItemsSource = property.Choices.Prepend(string.Empty).ToList(), DataContext = property, [!ComboBox.SelectedItemProperty] = new Binding(nameof(property.Value)), HorizontalAlignment = HorizontalAlignment.Stretch }
                : new TextBox { DataContext = property, [!TextBox.TextProperty] = new Binding(nameof(property.Value)) };
            Grid.SetRow(label, row);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, 1);
            grid.Children.Add(label);
            grid.Children.Add(input);
            row++;
        }

        var save = new Button { Content = Loc.Get("Properties.Save"), Classes = { "accent" }, Margin = new Thickness(0, 16, 0, 0) };
        save.Click += async (_, _) =>
        {
            if (vm.Save() && Saved is { } saved)
            {
                await saved(vm).ConfigureAwait(true);
            }
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = vm.Title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = vm.ProjectPath, Opacity = 0.6, FontSize = 12 },
                    grid,
                    save,
                },
            },
        };
    }
}
