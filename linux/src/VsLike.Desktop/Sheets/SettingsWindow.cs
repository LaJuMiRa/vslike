using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Settings window, Ctrl+, (SettingsView in SimpleSheets.swift).</summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly Preferences _preferences;
    private readonly ContentControl _page = new();
    private readonly ContentControl _tabs = new();
    private int _tab;

    private SettingsWindow(Workspace workspace)
    {
        _preferences = workspace.Preferences;
        Title = L.T("Settings…").TrimEnd('…');
        Width = 520;
        Height = 400;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.Res(this, BackgroundProperty, "WindowBackground");
        _tabs.HorizontalAlignment = HorizontalAlignment.Center;
        _tabs.Margin = new Thickness(0, 12, 0, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(_tabs, Dock.Top);
        dock.Children.Add(_tabs);
        dock.Children.Add(_page);
        Content = dock;
        Redraw();
        Closed += (_, _) => _open = null;
    }

    public static void Show(Workspace workspace, Window owner)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        _open = new SettingsWindow(workspace);
        _open.Show(owner);
    }

    private void Redraw()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        (string Title, string Symbol)[] tabs = [(L.T("General"), "gearshape"), ("StyleCop", "checkmark.seal"), (L.T("Editor"), "character.cursor.ibeam")];
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var selected = i == _tab;
            var face = new Border
            {
                Child = Ui.Label(tabs[i].Title, tabs[i].Symbol, selected ? "VsAccent" : "TextSecondary", 12),
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
            };
            if (selected)
            {
                Ui.Res(face, Border.BackgroundProperty, "FillSelected");
            }

            strip.Children.Add(Ui.Plain(face, () =>
            {
                _tab = index;
                Redraw();
            }));
        }

        _tabs.Content = strip;
        _page.Content = _tab switch
        {
            0 => General(),
            1 => StyleCop(),
            _ => EditorPage(),
        };
    }

    private Control General()
    {
        var p = _preferences;
        var home = AppPaths.Home;
        var shown = p.ProjectsFolder.StartsWith(home, StringComparison.Ordinal) ? "~" + p.ProjectsFolder[home.Length..] : p.ProjectsFolder;
        var folder = Ui.Text(shown, 13, "TextSecondary");
        folder.MaxWidth = 200;
        folder.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        var choose = Ui.PushButton(L.T("Choose…"), () => _ = ChooseFolder());
        return Form.Grouped(
            Form.Section(
                null,
                Form.Labeled(L.T("Appearance"), Form.Picker(
                    [(L.T("System"), Preferences.Appearance.System), (L.T("Light"), Preferences.Appearance.Light), (L.T("Dark"), Preferences.Appearance.Dark)],
                    p.AppearanceMode,
                    value => p.AppearanceMode = value)),
                Form.Labeled(L.T("Language"), Form.Picker(
                    [(L.T("System"), Preferences.Language.System), ("English", Preferences.Language.En), ("Deutsch", Preferences.Language.De)],
                    p.UiLanguage,
                    value => p.UiLanguage = value)),
                Form.Caption(L.T("The language changes after restarting VsLike.")),
                Form.Labeled(L.T("Folder for new solutions"), Ui.HStack(8, folder, choose)),
                Form.Toggle(L.T("StyleCop for new solutions"), p.StyleCopByDefault, value => p.StyleCopByDefault = value),
                Form.Caption(L.T("Each solution remembers its own setting in .vslike/settings.json; the toolbar button switches it."))));
    }

    private Control StyleCop()
    {
        var p = _preferences;
        var copyright = new TextBox { Text = p.StyleCopCopyrightText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 90, Width = 260 };
        copyright.TextChanged += (_, _) => p.StyleCopCopyrightText = copyright.Text ?? string.Empty;
        return Form.Grouped(
            Form.Section(
                null,
                Form.TextField(L.T("Company name"), p.StyleCopCompanyName, value => p.StyleCopCompanyName = value),
                Form.Labeled(L.T("Copyright text"), copyright),
                Form.Caption(L.T("Used when VsLike creates a stylecop.json for a solution (Code › StyleCop Settings…). {companyName} and {fileName} are replaced by StyleCop. An existing stylecop.json is edited directly and applies after saving."))));
    }

    private Control EditorPage()
    {
        var p = _preferences;
        var sample = Ui.Text($"{Preferences.EditorFontFamily.Split(',')[0]} {(int)p.FontSize} pt", p.FontSize, "TextPrimary", mono: true);
        var slider = new Slider { Minimum = 9, Maximum = 28, Value = p.FontSize, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220 };
        slider.ValueChanged += (_, e) =>
        {
            p.FontSize = Math.Round(e.NewValue);
            sample.FontSize = p.FontSize;
            sample.Text = $"{Preferences.EditorFontFamily.Split(',')[0]} {(int)p.FontSize} pt";
        };
        return Form.Grouped(
            Form.Section(
                null,
                Form.Labeled(L.T("Font size"), Ui.HStack(6, Ui.Text("9", 11, "TextSecondary"), slider, Ui.Text("28", 11, "TextSecondary"))),
                sample,
                Form.Toggle(L.T("Close brackets and quotes automatically"), p.AutoCloseBrackets, value => p.AutoCloseBrackets = value),
                Form.Toggle(L.T("Format document on save"), p.FormatOnSave, value => p.FormatOnSave = value),
                Form.Toggle(L.T("Show problems and breakpoints in the scroll bar"), p.ShowMinimapOfDiagnostics, value => p.ShowMinimapOfDiagnostics = value)));
    }

    private async Task ChooseFolder()
    {
        if (await OpenPanel.ChooseFolder(this, L.T("Choose where new solutions are created")) is { } folder)
        {
            _preferences.ProjectsFolder = folder;
            Redraw();
        }
    }
}
