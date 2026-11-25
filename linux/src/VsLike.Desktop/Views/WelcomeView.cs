using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Start screen: open or create a solution, recent solutions, and a check of the .NET SDK (WelcomeView.swift).</summary>
public sealed class WelcomeView : ReactiveView
{
    private readonly Workspace _workspace;
    private readonly MainWindow _window;
    private SdkInfo? _sdk;
    private bool _sdkChecked;

    public WelcomeView(Workspace workspace, MainWindow window)
    {
        _workspace = workspace;
        _window = window;
        Ui.Res(this, BackgroundProperty, "WindowBackground");
    }

    protected override IEnumerable<INotifyPropertyChanged?> Observed() => [_workspace.Preferences];

    protected override bool IsRelevant(object? sender, string? property) => property is nameof(Preferences.RecentSolutions) or null;

    protected override Control Render()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,320") };
        grid.Children.Add(Left());
        var divider = Ui.Divider(vertical: true);
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);
        var right = Right();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        if (!_sdkChecked)
        {
            _ = CheckSdk();
        }

        return grid;
    }

    private static Control Mark()
    {
        var tile = new Border
        {
            Width = 112,
            Height = 112,
            CornerRadius = new CornerRadius(26),
            BoxShadow = BoxShadows.Parse("0 8 14 0 #40000000"),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromRgb(51, 59, 74), 0), new GradientStop(Color.FromRgb(23, 26, 33), 1)],
            },
        };
        var icon = new SymbolIcon("chevron.left.forwardslash.chevron.right", 3.2)
        {
            Width = 56,
            Height = 56,
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromRgb(255, 204, 102), 0), new GradientStop(Color.FromRgb(232, 148, 43), 1)],
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tile.Child = icon;
        return tile;
    }

    private Control Left()
    {
        var title = Ui.Text("VsLike", 34, "TextPrimary", FontWeight.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var subtitle = Ui.Text(L.T("C# for the Mac"), 17, "TextSecondary");
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        var actions = Ui.VStack(
            10,
            Action(L.T("Open…"), L.T("Solution, project, folder or file"), "folder", "⌘O", () => _ = OpenPanel.ChooseAndOpen(_workspace, _window)),
            Action(L.T("New Solution…"), L.T("Console app, library, tests, web …"), "plus.square.on.square", "⇧⌘N", () => _workspace.IsNewSolutionPresented = true));
        actions.Width = 360;
        var middle = Ui.VStack(26, Mark(), Ui.VStack(6, title, subtitle), actions);
        foreach (var child in middle.Children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
        }

        middle.VerticalAlignment = VerticalAlignment.Center;
        var sdk = SdkStatus();
        sdk.HorizontalAlignment = HorizontalAlignment.Center;
        sdk.Margin = new Thickness(0, 0, 0, 22);
        var dock = new DockPanel();
        DockPanel.SetDock(sdk, Dock.Bottom);
        dock.Children.Add(sdk);
        dock.Children.Add(middle);
        return dock;
    }

    private Control Right()
    {
        var recent = _workspace.Preferences.RecentSolutions;
        var heading = Ui.Text(L.T("Recent"), 15, "TextPrimary", FontWeight.SemiBold);
        heading.Margin = new Thickness(20, 52, 20, 8);
        var dock = new DockPanel();
        DockPanel.SetDock(heading, Dock.Top);
        dock.Children.Add(heading);
        if (recent.Count == 0)
        {
            var hint = Ui.Text(L.T("Solutions you open appear here."), 13, "TextSecondary");
            hint.Margin = new Thickness(20, 0);
            hint.VerticalAlignment = VerticalAlignment.Top;
            dock.Children.Add(hint);
        }
        else
        {
            var rows = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
            foreach (var path in recent)
            {
                rows.Children.Add(RecentRow(path));
            }

            dock.Children.Add(new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        }

        var panel = new Border { Child = dock };
        Ui.Res(panel, Border.BackgroundProperty, "SidebarBackground");
        return panel;
    }

    private Control Action(string title, string subtitle, string symbol, string shortcut, Action action)
    {
        var icon = Icons.Make(symbol, 18, "VsAccent", 2);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Child = icon, Width = 34, Height = 34, CornerRadius = new CornerRadius(9) };
        Ui.Res(badge, Border.BackgroundProperty, "FillSelected");
        var texts = Ui.VStack(2, Ui.Text(title, 14, "TextPrimary", FontWeight.SemiBold), Ui.Text(subtitle, 11, "TextSecondary"));
        var row = Ui.Row([badge, texts], [Ui.Text(Shortcuts.Linux(shortcut), 11, "TextTertiary")], 14);
        var face = new Border { Child = row, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) };
        Ui.Res(face, Border.BackgroundProperty, "FillCapsule");
        face.PointerEntered += (_, _) => Ui.Res(face, Border.BackgroundProperty, "FillHover");
        face.PointerExited += (_, _) => Ui.Res(face, Border.BackgroundProperty, "FillCapsule");
        return Ui.Plain(face, action);
    }

    private Control RecentRow(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        var symbol = extension.StartsWith(".sln", StringComparison.Ordinal) ? "square.stack.3d.up.fill" : extension == ".csproj" ? "shippingbox.fill" : "folder.fill";
        var home = AppPaths.Home;
        var directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var shown = directory.StartsWith(home, StringComparison.Ordinal) ? "~" + directory[home.Length..] : directory;
        var icon = Icons.Make(symbol, 16, "VsAccent");
        icon.Width = 20;
        var name = Ui.Text(System.IO.Path.GetFileNameWithoutExtension(path), 13, "TextPrimary", FontWeight.Medium);
        var location = Ui.Text(shown, 11, "TextSecondary");
        location.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        var row = Ui.HStack(10, icon, Ui.VStack(1, name, location));
        var face = new Border { Child = row, Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        face.PointerEntered += (_, _) => Ui.Res(face, Border.BackgroundProperty, "FillHover");
        face.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        var button = Ui.Plain(face, () => _ = _workspace.Open(path));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return Ui.ContextMenu(button, () =>
        [
            new MenuEntry(L.T("Remove from List"), () => _workspace.Preferences.RecentSolutions = [.. _workspace.Preferences.RecentSolutions.Where(p => p != path)]),
            new MenuEntry(L.T("Reveal in Finder"), () => _workspace.RevealInFinder(path)),
        ]);
    }

    private Control SdkStatus()
    {
        if (!_sdkChecked)
        {
            return Ui.Spinner(14);
        }

        if (_sdk is { IsSupported: true } sdk)
        {
            return Ui.Label(L.T(".NET SDK %@", sdk.FullVersion), "checkmark.circle.fill", "TextSecondary", 11, 12);
        }

        var message = _sdk is null
            ? L.T("The .NET SDK is not installed")
            : L.T("VsLike needs the .NET SDK 10 or newer (found %@)", _sdk.FullVersion);
        var link = Ui.Plain(Ui.Text(L.T("Download the .NET SDK"), 11, "VsAccent"), () => OpenUrl("https://dotnet.microsoft.com/download"));
        return Ui.VStack(6, Ui.Label(message, "exclamationmark.triangle.fill", "VsWarning"), link);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No browser opener installed: nothing to open.
        }
    }

    private async Task CheckSdk()
    {
        try
        {
            _sdk = (await DotnetLocator.ListSdks(CommandRunner.Dotnet())).FirstOrDefault();
        }
        catch (CommandException)
        {
            _sdk = null;
        }

        _sdkChecked = true;
        Refresh();
    }
}
