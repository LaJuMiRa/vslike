using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>
/// SwiftUI <c>Form</c> with <c>.formStyle(.grouped)</c> (no Swift file of its own): section titles above rounded boxes
/// whose rows are separated by thin lines, labels on the left and values or controls on the right.
/// </summary>
public static class Form
{
    /// <summary>Sections stacked in a scrollable column.</summary>
    public static Control Grouped(params Control?[] sections)
    {
        var stack = Ui.VStack(18, sections);
        stack.Margin = new Thickness(16, 14);
        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    /// <summary>A section with an optional title; null rows are skipped.</summary>
    public static Control Section(string? title, params Control?[] rows)
    {
        var box = new StackPanel();
        var present = rows.OfType<Control>().ToList();
        for (var i = 0; i < present.Count; i++)
        {
            if (i > 0)
            {
                box.Children.Add(Ui.Divider(opacity: 0.7));
            }

            var row = present[i];
            row.Margin = new Thickness(10, 7);
            box.Children.Add(row);
        }

        var card = new Border { Child = box, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        Ui.Res(card, Border.BackgroundProperty, "FillCapsule");
        Ui.Res(card, Border.BorderBrushProperty, "Separator");
        if (title is null)
        {
            return card;
        }

        var header = Ui.Text(title, 13, "TextPrimary", FontWeight.SemiBold);
        header.Margin = new Thickness(4, 0, 0, 6);
        return Ui.VStack(0, header, card);
    }

    /// <summary>LabeledContent with a text value.</summary>
    public static Control Labeled(string label, string value)
    {
        var text = Ui.Text(value, 13, "TextSecondary");
        text.TextAlignment = TextAlignment.Right;
        return Labeled(label, text);
    }

    /// <summary>LabeledContent with a control on the right.</summary>
    public static Control Labeled(string label, Control content)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var title = Ui.Text(label);
        title.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(title);
        content.HorizontalAlignment = HorizontalAlignment.Right;
        content.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>A caption below a row (secondary, wrapping).</summary>
    public static TextBlock Caption(string text)
    {
        var block = Ui.Text(text, 11, "TextSecondary");
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    /// <summary>A picker (combo box) with (title, value) pairs and a change callback.</summary>
    public static ComboBox Picker<T>(IReadOnlyList<(string Title, T Value)> options, T selected, Action<T> changed)
    {
        var box = new ComboBox { ItemsSource = options.Select(o => o.Title).ToList(), MinWidth = 140 };
        var index = options.ToList().FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, selected));
        box.SelectedIndex = index;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < options.Count && !EqualityComparer<T>.Default.Equals(options[box.SelectedIndex].Value, selected))
            {
                selected = options[box.SelectedIndex].Value;
                changed(selected);
            }
        };
        return box;
    }

    /// <summary>A toggle row (SwiftUI Toggle in a grouped form: label left, switch right).</summary>
    public static Control Toggle(string label, bool value, Action<bool> changed)
    {
        var check = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        check.IsCheckedChanged += (_, _) => changed(check.IsChecked == true);
        return Labeled(label, check);
    }

    /// <summary>A text field row.</summary>
    public static Control TextField(string label, string value, Action<string> changed, string? placeholder = null)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder ?? label, MinWidth = 220 };
        box.TextChanged += (_, _) => changed(box.Text ?? string.Empty);
        return Labeled(label, box);
    }
}
