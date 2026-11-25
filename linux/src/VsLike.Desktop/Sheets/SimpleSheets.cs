using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>A text input sheet: new file, rename, breakpoint condition, go to line (PromptSheet in SimpleSheets.swift).</summary>
public sealed class PromptSheet : SheetView
{
    private readonly PromptRequest _request;
    private readonly TextBox _field;

    public PromptSheet(PromptRequest request, Action dismiss)
        : base(dismiss)
    {
        _request = request;
        _field = new TextBox { Text = request.InitialValue, PlaceholderText = request.Placeholder, FontFamily = Ui.Mono };
        _field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
        };
        var stack = Ui.VStack(14, Ui.Text(request.Title, 15, "TextPrimary", FontWeight.SemiBold));
        if (request.Message.Length > 0)
        {
            var message = Ui.Text(request.Message, 12, "TextSecondary");
            message.TextWrapping = TextWrapping.Wrap;
            message.TextTrimming = TextTrimming.None;
            stack.Children.Add(message);
        }

        stack.Children.Add(_field);
        stack.Children.Add(Buttons(null, request.ConfirmTitle, Confirm, dismiss));
        stack.Margin = new Thickness(20);
        stack.Width = 420;
        Content = stack;
    }

    protected override Control? FirstFocus() => _field;

    private void Confirm()
    {
        var text = _field.Text ?? string.Empty;
        Dismiss();
        _ = _request.Action(text);
    }
}

/// <summary>Definitions or references; click to open (ReferencesSheet).</summary>
public sealed class ReferencesSheet : SheetView
{
    public ReferencesSheet(Workspace workspace, ReferenceList list, Action dismiss)
        : base(dismiss)
    {
        var done = Ui.PushButton(L.T("Done"), dismiss);
        done.IsCancel = true;
        var header = Ui.Row([Ui.Text(list.Title, 15, "TextPrimary", FontWeight.SemiBold)], [done]);
        header.Margin = new Thickness(14);

        var rows = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
        foreach (var group in list.Locations.GroupBy(l => l.FilePath).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var title = Ui.Text(System.IO.Path.GetFileName(group.Key), 12, "TextSecondary", FontWeight.SemiBold);
            title.Margin = new Thickness(6, 8, 6, 3);
            rows.Children.Add(title);
            foreach (var location in group)
            {
                var captured = location;
                var line = Ui.Text(location.Line.ToString(CultureInfo.InvariantCulture), 11, "TextSecondary", mono: true);
                line.Width = 36;
                line.TextAlignment = TextAlignment.Right;
                var face = new Border
                {
                    Child = Ui.HStack(8, line, Ui.Text(location.LineText.Trim(), 12, "TextPrimary", mono: true)),
                    Padding = new Thickness(6, 3),
                    CornerRadius = new CornerRadius(5),
                    Background = Brushes.Transparent,
                };
                face.PointerEntered += (_, _) => Ui.Res(face, Border.BackgroundProperty, "FillHover");
                face.PointerExited += (_, _) => face.Background = Brushes.Transparent;
                Ui.OnTap(face, () =>
                {
                    dismiss();
                    workspace.Navigate(captured);
                });
                rows.Children.Add(face);
            }
        }

        var dock = new DockPanel { Width = 640, Height = 420 };
        DockPanel.SetDock(header, Dock.Top);
        var divider = Ui.Divider();
        DockPanel.SetDock(divider, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(divider);
        dock.Children.Add(new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = dock;
    }
}
