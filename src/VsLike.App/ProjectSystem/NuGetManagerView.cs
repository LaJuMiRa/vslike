using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.NuGet;

namespace VsLike.App.ProjectSystem;

/// <summary>Ansicht des NuGet-Managers: Reiter „Durchsuchen“ und „Installiert“.</summary>
public sealed class NuGetManagerView : UserControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not NuGetManagerViewModel vm)
        {
            return;
        }

        var query = new TextBox { Watermark = Loc.Get("NuGet.Search"), [!TextBox.TextProperty] = new Binding(nameof(vm.Query)) };
        query.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                await vm.SearchAsync().ConfigureAwait(true);
            }
        };
        var browseHeader = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new CheckBox { Content = Loc.Get("NuGet.Prerelease"), Margin = new Thickness(12, 0, 0, 0), [!CheckBox.IsCheckedProperty] = new Binding(nameof(vm.IncludePrerelease)), [DockPanel.DockProperty] = Avalonia.Controls.Dock.Right },
                query,
            },
        };
        var results = new ListBox
        {
            ItemsSource = vm.Results,
            [!ListBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedPackage)),
            ItemTemplate = new FuncDataTemplate<NuGetPackage>((p, _) => new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = p?.Id, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = p?.LatestVersion, Opacity = 0.6 }, new TextBlock { Text = p?.IsVerified == true ? "✓" : string.Empty, Opacity = 0.6 } } },
                    new TextBlock { Text = p?.Description, TextWrapping = TextWrapping.Wrap, MaxHeight = 40, Opacity = 0.75, FontSize = 12 },
                    new TextBlock { Text = p is null ? string.Empty : Loc.Format("NuGet.Downloads", p.TotalDownloads), Opacity = 0.5, FontSize = 11 },
                },
            }),
        };
        var install = new Button { Content = Loc.Get("NuGet.Install"), Classes = { "accent" }, Command = vm.InstallCommand };
        var versions = new ComboBox { ItemsSource = vm.Versions, MinWidth = 160, [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedVersion)) };
        var browseFooter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { new TextBlock { Text = Loc.Get("NuGet.Version"), VerticalAlignment = VerticalAlignment.Center }, versions, install } };
        var browse = new DockPanel { Children = { browseHeader, browseFooter, results } };
        DockPanel.SetDock(browseHeader, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(browseFooter, Avalonia.Controls.Dock.Bottom);

        var installed = new ListBox
        {
            ItemsSource = vm.Installed,
            [!ListBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedInstalled)),
            ItemTemplate = new FuncDataTemplate<InstalledPackage>((p, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = p?.Id, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = p?.ResolvedVersion ?? p?.RequestedVersion, Opacity = 0.7 },
                    new TextBlock { Text = p?.HasUpdate == true ? "→ " + p.LatestVersion : string.Empty, Foreground = Brushes.Goldenrod },
                },
            }),
        };
        var installedFooter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { new Button { Content = Loc.Get("NuGet.Update"), Command = vm.UpdateCommand }, new Button { Content = Loc.Get("NuGet.Uninstall"), Command = vm.UninstallCommand } },
        };
        var installedPanel = new DockPanel { Children = { installedFooter, installed } };
        DockPanel.SetDock(installedFooter, Avalonia.Controls.Dock.Bottom);

        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = Loc.Get("NuGet.Browse"), Content = browse },
                new TabItem { Header = Loc.Get("NuGet.Installed"), Content = installedPanel },
            },
        };
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0), [!TextBlock.TextProperty] = new Binding(nameof(vm.Status)) };
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(16), Children = { status, tabs } };
        _ = vm.LoadInstalledAsync();
    }
}
