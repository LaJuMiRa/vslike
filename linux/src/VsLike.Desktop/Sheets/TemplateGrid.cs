using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Project templates as selectable cards (TemplateGrid in SimpleSheets.swift).</summary>
public sealed class TemplateGrid : UserControl
{
    public static readonly IReadOnlyList<ProjectTemplate> Fallback =
    [
        new("Console App", "console", "Common/Console"),
        new("Class Library", "classlib", "Common/Library"),
        new("xUnit Test Project", "xunit", "Test"),
        new("ASP.NET Core Web API", "webapi", "Web"),
        new("Worker Service", "worker", "Common/Worker"),
    ];

    private readonly WrapPanel _wrap = new();
    private readonly Control _spinner = new Spinner { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<ProjectTemplate> _templates = [];

    public TemplateGrid(string selection)
    {
        Selection = selection;
        Height = 230;
        Content = new Panel { Children = { new ScrollViewer { Content = _wrap }, _spinner } };
    }

    public string Selection { get; private set; }

    public void SetTemplates(IReadOnlyList<ProjectTemplate> templates)
    {
        _templates = templates;
        _spinner.IsVisible = templates.Count == 0;
        Rebuild();
    }

    private static string SymbolFor(ProjectTemplate template)
    {
        var tags = (template.Tags + " " + template.ShortName).ToLowerInvariant();
        if (tags.Contains("test", StringComparison.Ordinal) || template.ShortName is "xunit" or "nunit" or "mstest")
        {
            return "checkmark.diamond";
        }

        if (tags.Contains("web", StringComparison.Ordinal) || tags.Contains("blazor", StringComparison.Ordinal) || tags.Contains("razor", StringComparison.Ordinal) || tags.Contains("mvc", StringComparison.Ordinal))
        {
            return "globe";
        }

        if (tags.Contains("console", StringComparison.Ordinal))
        {
            return "terminal";
        }

        if (tags.Contains("library", StringComparison.Ordinal))
        {
            return "books.vertical";
        }

        if (tags.Contains("worker", StringComparison.Ordinal) || tags.Contains("service", StringComparison.Ordinal))
        {
            return "gearshape.2";
        }

        return tags.Contains("grpc", StringComparison.Ordinal) ? "network" : "shippingbox";
    }

    private void Rebuild()
    {
        _wrap.Children.Clear();
        foreach (var template in _templates)
        {
            _wrap.Children.Add(Card(template));
        }
    }

    private Control Card(ProjectTemplate template)
    {
        var selected = template.ShortName == Selection;
        var icon = Icons.Make(SymbolFor(template), 17, selected ? "IconKnockout" : "VsAccent");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Child = icon, Width = 30, Height = 30, CornerRadius = new CornerRadius(8) };
        Ui.Res(badge, Border.BackgroundProperty, selected ? "VsAccent" : "FillSelected");
        var texts = Ui.VStack(2, Ui.Text(template.Name, 13, "TextPrimary", FontWeight.Medium), Ui.Text(template.ShortName, 11, "TextSecondary", mono: true));
        var face = new Border
        {
            Child = Ui.HStack(10, badge, texts),
            Width = 190,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
        };
        Ui.Res(face, Border.BackgroundProperty, selected ? "FillSelected" : "FillCapsule");
        if (selected)
        {
            Ui.Res(face, Border.BorderBrushProperty, "VsAccent");
        }

        Ui.OnTap(face, () =>
        {
            Selection = template.ShortName;
            Rebuild();
        });
        return face;
    }
}
