using System.Globalization;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>NuGet package manager for one project: browse, installed, updates (NuGetSheet in ProjectSheets.swift).</summary>
public sealed class NuGetSheet : SheetView
{
    private readonly Workspace _workspace;
    private readonly string _project;
    private readonly NuGetClient _client = new();
    private readonly TextBox _query = new() { PlaceholderText = L.T("Search nuget.org"), MinWidth = 0 };
    private readonly CheckBox _prerelease = new() { Content = L.T("Pre-release") };
    private readonly ContentControl _list = new();
    private readonly ContentControl _detail = new();
    private readonly ContentControl _tabs = new();
    private readonly TextBlock _message = Ui.Text(string.Empty, 11, "TextSecondary");
    private readonly Control _search;
    private Tab _tab = Tab.Browse;
    private IReadOnlyList<NuGetPackage> _results = [];
    private IReadOnlyList<InstalledPackage> _installed = [];
    private IReadOnlyList<string> _versions = [];
    private string? _selected;
    private string _version = string.Empty;
    private bool _isWorking;

    public NuGetSheet(Workspace workspace, string project, Action dismiss)
        : base(dismiss)
    {
        _workspace = workspace;
        _project = project;
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = Search();
                e.Handled = true;
            }
        };
        _search = Ui.Row([], [_prerelease], 8);
        ((DockPanel)_search).LastChildFill = true;
        ((DockPanel)_search).Children.Insert(0, _query);
        _search.Margin = new Thickness(10, 10, 10, 0);
        _message.Margin = new Thickness(10);

        var name = System.IO.Path.GetFileNameWithoutExtension(project);
        var done = Ui.PushButton(L.T("Done"), dismiss);
        done.IsCancel = true;
        var header = Ui.Row(
            [Icons.Make("cube.box.fill", 20, "VsAccent"), Ui.Text(L.T("NuGet Packages · %@", name), 15, "TextPrimary", FontWeight.SemiBold)],
            [_tabs, done],
            10);
        header.Margin = new Thickness(14);

        var left = new DockPanel { Width = 360 };
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        left.Children.Add(_list);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("360,Auto,*") };
        body.Children.Add(left);
        var divider = Ui.Divider(vertical: true);
        Grid.SetColumn(divider, 1);
        body.Children.Add(divider);
        Grid.SetColumn(_detail, 2);
        body.Children.Add(_detail);

        var root = new DockPanel { Width = 820, Height = 520 };
        DockPanel.SetDock(header, Dock.Top);
        var rule = Ui.Divider();
        DockPanel.SetDock(rule, Dock.Top);
        DockPanel.SetDock(_message, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(rule);
        root.Children.Add(_message);
        root.Children.Add(body);
        Content = root;
        Redraw();
        _ = LoadInstalled();
    }

    private enum Tab
    {
        Browse,
        Installed,
        Updates,
    }

    private IEnumerable<(string Id, string Version, string Detail, bool Verified)> Rows() => _tab switch
    {
        Tab.Browse => _results.Select(r => (r.Id, r.LatestVersion, r.Description ?? string.Empty, r.IsVerified)),
        Tab.Installed => _installed.Select(i => (i.Id, i.ResolvedVersion ?? i.RequestedVersion ?? string.Empty, i.HasUpdate ? L.T("Update available: %@", i.LatestVersion ?? string.Empty) : string.Empty, false)),
        _ => _installed.Where(i => i.HasUpdate).Select(i => (i.Id, $"{i.ResolvedVersion} → {i.LatestVersion}", string.Empty, false)),
    };

    private void Redraw()
    {
        _search.IsVisible = _tab == Tab.Browse;
        _tabs.Content = TabSwitcher();
        var rows = new StackPanel { Margin = new Thickness(6) };
        foreach (var row in Rows())
        {
            rows.Children.Add(Row(row));
        }

        _list.Content = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _detail.Content = Detail();
    }

    private Control TabSwitcher()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var (tab, title) in new[] { (Tab.Browse, L.T("Browse")), (Tab.Installed, L.T("Installed")), (Tab.Updates, L.T("Updates")) })
        {
            var selected = tab == _tab;
            var face = new Border { Child = Ui.Text(title, 12, "TextPrimary", selected ? FontWeight.SemiBold : FontWeight.Normal), Padding = new Thickness(10, 3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
            if (selected)
            {
                Ui.Res(face, Border.BackgroundProperty, "ControlBackground");
            }

            var captured = tab;
            strip.Children.Add(Ui.Plain(face, () =>
            {
                _tab = captured;
                Redraw();
                _ = LoadInstalled();
            }));
        }

        return Ui.Rounded(strip, "FillCapsule", 6, new Thickness(1));
    }

    private Control Row((string Id, string Version, string Detail, bool Verified) row)
    {
        var title = Ui.HStack(6, Ui.Text(row.Id, 13, "TextPrimary", FontWeight.Medium));
        if (row.Verified)
        {
            title.Children.Add(Icons.Make("checkmark.seal.fill", 11, "VsInfo"));
        }

        var stack = Ui.VStack(2, Ui.Row([title], [Ui.Text(row.Version, 11, "TextSecondary", mono: true)]));
        if (row.Detail.Length > 0)
        {
            var detail = Ui.Text(row.Detail, 11, "TextSecondary");
            detail.TextWrapping = TextWrapping.Wrap;
            detail.MaxHeight = 30;
            stack.Children.Add(detail);
        }

        var face = new Border { Child = stack, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        if (_selected == row.Id)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        Ui.OnTap(face, () =>
        {
            _selected = row.Id;
            Redraw();
            _ = LoadVersions();
        });
        return face;
    }

    private Control Detail()
    {
        if (_selected is not { } selected)
        {
            return Ui.Unavailable(L.T("Select a package"), "cube.box");
        }

        var current = _installed.FirstOrDefault(i => i.Id == selected);
        var stack = Ui.VStack(12, Ui.Text(selected, 17, "TextPrimary", FontWeight.Bold));
        if (_results.FirstOrDefault(r => r.Id == selected) is { } package)
        {
            if (package.Authors is { } authors)
            {
                stack.Children.Add(Ui.Text(authors, 13, "TextSecondary"));
            }

            stack.Children.Add(Ui.Text(L.T("%@ downloads", package.TotalDownloads.ToString("N0", CultureInfo.CurrentCulture)), 11, "TextSecondary"));
            if (package.Description is { } description)
            {
                var text = Ui.Text(description, 12, "TextPrimary");
                text.TextWrapping = TextWrapping.Wrap;
                text.TextTrimming = TextTrimming.None;
                stack.Children.Add(text);
            }
        }

        if (current is not null)
        {
            stack.Children.Add(Ui.Label(L.T("Installed: %@", current.ResolvedVersion ?? current.RequestedVersion ?? string.Empty), "checkmark.circle.fill", "VsSuccess"));
        }

        stack.Children.Add(Form.Picker([.. _versions.Select(v => (v, v))], _version, v => _version = v));
        var buttons = Ui.HStack(8, Ui.PushButton(current is null ? L.T("Install") : L.T("Install Version"), () => _ = Install(selected), prominent: true));
        buttons.Children[0].IsEnabled = !_isWorking;
        if (current is not null)
        {
            var uninstall = Ui.PushButton(L.T("Uninstall"), () => _ = Uninstall(selected));
            uninstall.IsEnabled = !_isWorking;
            buttons.Children.Add(uninstall);
        }

        if (_isWorking)
        {
            buttons.Children.Add(Ui.Spinner(14));
        }

        stack.Children.Add(buttons);
        stack.Margin = new Thickness(16);
        return stack;
    }

    private void Report(string text)
    {
        _message.Text = text;
        _isWorking = false;
        Redraw();
    }

    private async Task Search()
    {
        var query = _query.Text ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        _isWorking = true;
        try
        {
            _results = await _client.Search(query, _prerelease.IsChecked == true);
            Report(string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Report(ex.Message);
        }
    }

    private async Task LoadInstalled()
    {
        try
        {
            var service = new ProjectService(CommandRunner.Dotnet());
            var listed = await service.ListPackages(_project, outdated: _tab == Tab.Updates);
            if (_tab != Tab.Updates)
            {
                var outdated = await service.ListPackages(_project, outdated: true);
                listed = [.. listed.Select(p => outdated.FirstOrDefault(o => o.Id == p.Id) ?? p)];
            }

            _installed = listed;
            Redraw();
        }
        catch (CommandException)
        {
            // Keep the list shown so far.
        }
    }

    private async Task LoadVersions()
    {
        if (_selected is not { } selected)
        {
            return;
        }

        try
        {
            _versions = await _client.Versions(selected, _prerelease.IsChecked == true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _versions = [];
        }

        _version = _versions.FirstOrDefault() ?? string.Empty;
        Redraw();
    }

    private async Task Change(Func<ProjectService, Task> action, string done)
    {
        _isWorking = true;
        Redraw();
        try
        {
            await action(new ProjectService(CommandRunner.Dotnet()));
            _message.Text = done;
            await LoadInstalled();
            _workspace.RefreshTree();
            await _workspace.RestartEngine();
            _isWorking = false;
            Redraw();
        }
        catch (CommandException ex)
        {
            Report(ex.Message);
        }
    }

    private Task Install(string id) =>
        Change(service => service.AddPackage(id, _version, _project), L.T("%@ %@ installed.", id, _version));

    private Task Uninstall(string id) =>
        Change(service => service.RemovePackage(id, _project), L.T("%@ removed.", id));
}
