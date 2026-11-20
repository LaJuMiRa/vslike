using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Core.Code;

namespace VsLike.Editor.Intelligence;

/// <summary>Baut den Inhalt des Hover-Tooltips (Signatur, Doku, Diagnosen).</summary>
public static class QuickInfoPresenter
{
    public static Control? Create(QuickInfo? info, IReadOnlyList<DiagnosticInfo> diagnosticsAtPosition, FontFamily monoFont)
    {
        var diagnostics = (info?.Diagnostics ?? []).Concat(diagnosticsAtPosition).DistinctBy(d => (d.Id, d.Start, d.Message)).ToList();
        if ((info is null || string.IsNullOrWhiteSpace(info.Signature)) && diagnostics.Count == 0)
        {
            return null;
        }

        var panel = new StackPanel { Spacing = 6, MaxWidth = 640 };
        if (info is not null && !string.IsNullOrWhiteSpace(info.Signature))
        {
            panel.Children.Add(new SelectableTextBlock { Text = info.Signature, FontFamily = monoFont, TextWrapping = TextWrapping.Wrap });
        }

        if (info?.Documentation is { Length: > 0 } documentation)
        {
            panel.Children.Add(new TextBlock { Text = documentation, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }

        foreach (var diagnostic in diagnostics)
        {
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = diagnostic.Id, FontWeight = FontWeight.SemiBold, Foreground = SeverityBrush(diagnostic.Severity) },
                    new TextBlock { Text = diagnostic.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                },
            });
        }

        return new Border { Padding = new Thickness(2), Child = panel };
    }

    private static IBrush SeverityBrush(DiagnosticSeverityKind severity) => severity switch
    {
        DiagnosticSeverityKind.Error => new SolidColorBrush(Color.Parse("#F07167")),
        DiagnosticSeverityKind.Warning => new SolidColorBrush(Color.Parse("#D9A93A")),
        _ => new SolidColorBrush(Color.Parse("#6CA6F0")),
    };
}
