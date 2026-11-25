using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VsLike.Desktop;

/// <summary>
/// Sheets of the main window (the <c>.sheet</c> and <c>.alert</c> modifiers of MainView.swift): one card at a time,
/// attached to the top of the window like a macOS sheet, above a dimmed window.
/// </summary>
public sealed class SheetHost : ReactiveView
{
    private static readonly HashSet<string> SheetProperties =
    [
        nameof(Workspace.IsNewSolutionPresented), nameof(Workspace.NewProjectTarget), nameof(Workspace.NugetProject),
        nameof(Workspace.PropertiesProject), nameof(Workspace.Prompt), nameof(Workspace.References), nameof(Workspace.Alert),
    ];

    private readonly Workspace _workspace;
    private readonly MainWindow _window;

    public SheetHost(Workspace workspace, MainWindow window)
    {
        _workspace = workspace;
        _window = window;
    }

    protected override IEnumerable<System.ComponentModel.INotifyPropertyChanged?> Observed() => [_workspace];

    protected override bool IsRelevant(object? sender, string? property) => property is null || SheetProperties.Contains(property);

    protected override Control Render()
    {
        var w = _workspace;
        if (w.Alert is { } alert)
        {
            w.Alert = null;
            _ = _window.ShowAlert(alert.Title, alert.Message);
        }

        Control? sheet = null;
        if (w.IsNewSolutionPresented)
        {
            sheet = new NewSolutionSheet(w, () => w.IsNewSolutionPresented = false);
        }
        else if (w.NewProjectTarget is { } solution)
        {
            sheet = new NewProjectSheet(w, solution, () => w.NewProjectTarget = null);
        }
        else if (w.NugetProject is { } nuget)
        {
            sheet = new NuGetSheet(w, nuget, () => w.NugetProject = null);
        }
        else if (w.PropertiesProject is { } properties)
        {
            sheet = new ProjectPropertiesSheet(w, properties, () => w.PropertiesProject = null);
        }
        else if (w.Prompt is { } prompt)
        {
            sheet = new PromptSheet(prompt, () => w.Prompt = null);
        }
        else if (w.References is { } references)
        {
            sheet = new ReferencesSheet(w, references, () => w.References = null);
        }

        return sheet is null ? new Panel { IsHitTestVisible = false } : Modal.Overlay(sheet, top: true);
    }
}

/// <summary>Scrim and card for sheets, alerts and the palette.</summary>
public static class Modal
{
    /// <summary>A card over a dimmed window; <paramref name="top"/> attaches it to the top edge like a sheet.</summary>
    public static Control Overlay(Control content, bool top, Action? tapOutside = null)
    {
        var scrim = new Border();
        Ui.Res(scrim, Border.BackgroundProperty, "Scrim");
        if (tapOutside is not null)
        {
            scrim.PointerPressed += (_, e) =>
            {
                tapOutside();
                e.Handled = true;
            };
        }

        var card = Card(content, top ? 12 : 14);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        card.Margin = new Thickness(0, top ? TitleBar.Height : 0, 0, 0);
        return new Panel { Children = { scrim, card } };
    }

    public static Border Card(Control content, double radius)
    {
        var card = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 16 30 0 #47000000"),
            ClipToBounds = false,
        };
        Ui.Res(card, Border.BackgroundProperty, "WindowBackground");
        Ui.Res(card, Border.BorderBrushProperty, "Separator");
        return card;
    }
}

/// <summary>NSAlert as a card in the window: message, informative text and buttons; the first button is the default.</summary>
public static class Alerts
{
    public static Task<T> Ask<T>(Panel layer, string message, string? information, IReadOnlyList<(string Title, T Value, bool IsDefault)> buttons, T cancelValue)
    {
        var completion = new TaskCompletionSource<T>();
        Control? overlay = null;
        void Finish(T value)
        {
            if (overlay is not null)
            {
                layer.Children.Remove(overlay);
            }

            completion.TrySetResult(value);
        }

        var icon = new Image
        {
            Width = 48,
            Height = 48,
            Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://vslike/Assets/vslike-256.png"))),
        };
        var title = Ui.Text(message, 13, "TextPrimary", FontWeight.Bold);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextAlignment = TextAlignment.Center;
        var stack = Ui.VStack(10, icon, title);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        if (information is not null)
        {
            stack.Children.Add(Ui.Wrap(Ui.Text(information, 11, "TextPrimary")));
        }

        var buttonStack = Ui.VStack(6);
        buttonStack.Margin = new Thickness(0, 8, 0, 0);
        foreach (var (text, value, isDefault) in buttons)
        {
            var button = Ui.PushButton(text, () => Finish(value), isDefault);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.IsDefault = isDefault;
            buttonStack.Children.Add(button);
        }

        stack.Children.Add(buttonStack);
        stack.Width = 260;
        stack.Margin = new Thickness(16);
        overlay = Modal.Overlay(stack, top: false);
        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(cancelValue);
                e.Handled = true;
            }
        };
        layer.Children.Add(overlay);
        return completion.Task;
    }
}

/// <summary>Base for sheet contents: a fixed-width card body with a dismiss action.</summary>
public abstract class SheetView : UserControl
{
    protected SheetView(Action dismiss)
    {
        Dismiss = dismiss;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
                e.Handled = true;
            }
        };
    }

    protected Action Dismiss { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FirstFocus()?.Focus());
    }

    /// <summary>The control that gets the keyboard focus when the sheet appears.</summary>
    protected virtual Control? FirstFocus() => this;

    /// <summary>Cancel and confirm buttons on the right (with Escape/Return), plus an optional leading control.</summary>
    protected static Control Buttons(Control? leading, string confirmTitle, Action confirm, Action cancel, Func<bool>? enabled = null, Button? confirmButton = null)
    {
        var cancelButton = Ui.PushButton(L.T("Cancel"), cancel);
        cancelButton.IsCancel = true;
        var ok = confirmButton ?? Ui.PushButton(confirmTitle, confirm, prominent: true);
        ok.IsDefault = true;
        if (enabled is not null)
        {
            ok.IsEnabled = enabled();
        }

        return Ui.Row(leading is null ? [] : [leading], [cancelButton, ok], 8);
    }
}
