using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop.Diagram;

/// <summary>
/// Edits the selected diagram type: name, access, modifiers, base types and members (DiagramInspector.swift). Every
/// change is written into the code by the engine and can be undone in the diagram.
/// </summary>
public sealed class DiagramInspector : UserControl
{
    private TypeInspector? _typeInspector;

    public DiagramInspector(DiagramEditorModel model)
    {
        Model = model;
        Update();
    }

    public DiagramEditorModel Model { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Model.PropertyChanged += OnModelChanged;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Model.PropertyChanged -= OnModelChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagramEditorModel.SelectedType) or nameof(DiagramEditorModel.Selection) or nameof(DiagramEditorModel.Model)
            or nameof(DiagramEditorModel.Boxes) or nameof(DiagramEditorModel.ShowsExternal))
        {
            Update();
        }
    }

    private void Update()
    {
        if (Model.SelectedType is { } type)
        {
            // Keyed by type only, so the chosen member kind survives adding a member.
            if (_typeInspector?.FullName != type.FullName)
            {
                _typeInspector = new TypeInspector(Model, type);
            }
            else
            {
                _typeInspector.Update(type);
            }

            Content = new ScrollViewer { Content = _typeInspector, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            return;
        }

        _typeInspector = null;
        Content = NoSelection();
    }

    private Control NoSelection()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(0, 40, 0, 0) };
        var unavailable = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        unavailable.Children.Add(new SymbolIcon("rectangle.3.group", 40) { Foreground = DiagramColors.Brush(DiagramColors.Secondary), HorizontalAlignment = HorizontalAlignment.Center });
        unavailable.Children.Add(new TextBlock { Text = L.T("No Type Selected"), FontSize = 17, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        unavailable.Children.Add(new TextBlock
        {
            Text = L.T("Click a type on the diagram to edit it."),
            Foreground = DiagramColors.Brush(DiagramColors.Secondary),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16, 0),
        });
        panel.Children.Add(unavailable);

        var info = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        info.Children.Add(new TextBlock { Text = L.T("Diagram"), FontWeight = FontWeight.Bold });
        info.Children.Add(InspectorRows.Labeled(L.T("Types"), new TextBlock { Text = Model.Boxes.Count.ToString(L.Culture) }));
        info.Children.Add(InspectorRows.Labeled(L.T("Relations"), new TextBlock { Text = Model.Edges.Count.ToString(L.Culture) }));
        info.Children.Add(InspectorRows.Toggle(L.T("Show external types"), Model.ShowsExternal, on => Model.ShowsExternal = on));
        panel.Children.Add(info);
        return panel;
    }
}

/// <summary>Rows and sections in the style of a macOS grouped form (<c>.formStyle(.grouped)</c>).</summary>
internal static class InspectorRows
{
    public const double MonoSize = 11.5;

    public static Control Section(string? title, IEnumerable<Control> rows)
    {
        var stack = new StackPanel();
        if (title is not null)
        {
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 0, 6) });
        }

        var box = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            if (!first)
            {
                box.Children.Add(new Border { Height = 1, Background = DiagramColors.Brush(DiagramColors.Separator), Margin = new Thickness(10, 0) });
            }

            row.Margin = new Thickness(10, 6);
            box.Children.Add(row);
            first = false;
        }

        stack.Children.Add(new Border
        {
            Child = box,
            CornerRadius = new CornerRadius(8),
            Background = DiagramColors.Brush(DiagramColors.Primary, DiagramColors.IsDark ? 0.05 : 0.03),
            BorderBrush = DiagramColors.Brush(DiagramColors.Primary, 0.08),
            BorderThickness = new Thickness(1),
        });
        return stack;
    }

    /// <summary>Label on the left, control on the right (LabeledContent, Picker, TextField with a title).</summary>
    public static Control Labeled(string title, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 22 };
        grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        control.HorizontalAlignment = control is TextBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>A Toggle in a grouped form: title left, switch right.</summary>
    public static Control Toggle(string title, bool isOn, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = isOn, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return Labeled(title, toggle);
    }

    public static Control Picker<T>(string title, IReadOnlyList<(T Value, string Title)> options, T selected, Action<T> changed)
    {
        var combo = new ComboBox { ItemsSource = options.Select(o => o.Title).ToList(), MinWidth = 120 };
        combo.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, selected)));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && !EqualityComparer<T>.Default.Equals(options[combo.SelectedIndex].Value, selected))
            {
                changed(options[combo.SelectedIndex].Value);
            }
        };
        return Labeled(title, combo);
    }

    public static Button IconButton(string symbol, string? help, Action action)
    {
        var button = new Button
        {
            Content = new SymbolIcon(symbol, 15),
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = default,
        };
        if (help is not null)
        {
            ToolTip.SetTip(button, help);
        }

        button.Click += (_, _) => action();
        return button;
    }

    public static Button TextButton(string title, string symbol, Action action, bool destructive = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new SymbolIcon(symbol, 14));
        row.Children.Add(new TextBlock { Text = title });
        var button = new Button { Content = row, Background = Brushes.Transparent, BorderThickness = default, Padding = default };
        if (destructive)
        {
            button.Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0x54, 0x4F));
        }

        button.Click += (_, _) => action();
        return button;
    }
}
