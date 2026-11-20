using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VsLike.Core.Localization;

namespace VsLike.App.Debugging;

/// <summary>Programmausgabe der Debug-Sitzung und Eingabezeile zum Auswerten von Ausdrücken.</summary>
public sealed class DebugConsoleView : UserControl
{
    private static readonly FontFamily Mono = new("JetBrains Mono, Menlo, DejaVu Sans Mono, monospace");

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not DebugConsoleViewModel { Controller: var controller })
        {
            return;
        }

        var list = new ListBox
        {
            ItemsSource = controller.Console,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<string>((line, _) => new TextBlock { Text = line, FontFamily = Mono, FontSize = 12 }),
        };
        controller.Console.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (controller.Console.Count > 0)
            {
                list.ScrollIntoView(controller.Console.Count - 1);
            }
        });
        var input = new TextBox { Watermark = Loc.Get("Debug.Evaluate"), FontFamily = Mono, FontSize = 12, Margin = new Thickness(6) };
        input.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && input.Text is { Length: > 0 } text)
            {
                input.Text = string.Empty;
                await controller.EvaluateInConsoleAsync(text).ConfigureAwait(true);
            }
        };
        var root = new DockPanel();
        DockPanel.SetDock(input, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(input);
        root.Children.Add(list);
        Content = root;
    }
}
