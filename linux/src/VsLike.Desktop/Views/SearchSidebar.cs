using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace VsLike.Desktop;

/// <summary>Find and replace in all files (SearchSidebar in SearchAndTests.swift).</summary>
public sealed class SearchSidebar : UserControl
{
    private readonly Workspace _workspace;
    private readonly SearchModel _search;
    private readonly TextBox _query;
    private readonly StackPanel _replaceRow;
    private bool _showsReplace;

    public SearchSidebar(Workspace workspace, SearchModel search)
    {
        _workspace = workspace;
        _search = search;

        _query = new TextBox { Text = search.Query, PlaceholderText = L.T("Search") };
        _query.TextChanged += (_, _) => search.Query = _query.Text ?? string.Empty;
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                search.Run();
                e.Handled = true;
            }
        };

        var replacement = new TextBox { Text = search.Replacement, PlaceholderText = L.T("Replace"), MinWidth = 0 };
        replacement.TextChanged += (_, _) => search.Replacement = replacement.Text ?? string.Empty;
        var replaceAll = Ui.IconButton("arrow.2.squarepath", search.ReplaceAll, L.T("Replace All"));
        _replaceRow = Ui.HStack(4, replacement, replaceAll);
        _replaceRow.IsVisible = false;
        replacement.Width = 150;

        var disclosure = Ui.Plain(Icons.Make("chevron.right", 11, "TextSecondary", 2.2), ToggleReplace);
        var fields = Ui.VStack(6, _query, _replaceRow);
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(disclosure, Dock.Left);
        disclosure.Margin = new Thickness(0, 0, 6, 0);
        disclosure.VerticalAlignment = VerticalAlignment.Top;
        disclosure.Padding = new Thickness(0, 6, 0, 0);
        top.Children.Add(disclosure);
        top.Children.Add(fields);

        var options = new Live(Options, search) { Only = new HashSet<string> { nameof(SearchModel.CaseSensitive), nameof(SearchModel.WholeWord), nameof(SearchModel.UseRegex), nameof(SearchModel.Results), nameof(SearchModel.IsSearching) } };
        var error = new Live(
            () => search.ErrorText is { } text ? Ui.Text(text, 11, "VsError") : new Panel(),
            search) { Only = new HashSet<string> { nameof(SearchModel.ErrorText) } };

        var header = Ui.VStack(8, top, options, error);
        header.Margin = new Thickness(12, 0, 12, 8);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new Live(Results, search) { Only = new HashSet<string> { nameof(SearchModel.Results) } });
        Content = dock;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => _query.Focus());
    }

    private void ToggleReplace()
    {
        _showsReplace = !_showsReplace;
        _replaceRow.IsVisible = _showsReplace;
    }

    private Control Options()
    {
        var status = _search.IsSearching
            ? Ui.Spinner(10)
            : _search.Results.Count > 0
                ? Ui.Text(L.T("%lld in %lld files", _search.MatchCount, _search.Results.Count), 11, "TextSecondary")
                : new Panel();
        var toggles = Ui.HStack(
            4,
            Toggle("textformat", "Match Case", _search.CaseSensitive, value => _search.CaseSensitive = value),
            Toggle("text.word.spacing", "Whole Word", _search.WholeWord, value => _search.WholeWord = value),
            Toggle("asterisk", "Regular Expression", _search.UseRegex, value => _search.UseRegex = value));
        return Ui.Row([toggles], [status]);
    }

    private Control Toggle(string symbol, string help, bool isOn, Action<bool> set)
    {
        var icon = Icons.Make(symbol, 13, isOn ? "VsAccent" : "TextSecondary");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Child = icon, Width = 24, Height = 20, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
        if (isOn)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        void Flip()
        {
            set(!isOn);
            _search.Run();
        }

        return Ui.Plain(face, Flip, L.T(help));
    }

    private Control Results()
    {
        var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
        foreach (var file in _search.Results)
        {
            var name = System.IO.Path.GetFileName(file.Path);
            var header = Ui.HStack(
                5,
                Icons.Make(FileNode.SymbolForFile(file.Path), 12, "TextSecondary"),
                Ui.Text(name, 12, "TextPrimary", FontWeight.SemiBold),
                Ui.Text(file.Matches.Count.ToString(CultureInfo.InvariantCulture), 12, "TextSecondary"));
            header.Margin = new Thickness(6, 8, 6, 3);
            rows.Children.Add(header);
            foreach (var match in file.Matches)
            {
                rows.Children.Add(MatchRow(file.Path, match));
            }
        }

        return new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private Control MatchRow(string path, SearchModel.Match match)
    {
        var line = Ui.Text(match.Line.ToString(CultureInfo.InvariantCulture), 10, "TextTertiary", mono: true);
        line.Width = 30;
        line.TextAlignment = TextAlignment.Right;
        var row = Ui.HStack(6, line, Ui.Text(match.Preview, 11, "TextPrimary", mono: true));
        var face = new Border { Child = row, Height = 20, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
        face.PointerEntered += (_, _) => Ui.Res(face, Border.BackgroundProperty, "FillHover");
        face.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        Ui.OnTap(face, () => _workspace.OpenFile(path, match.Line, match.Column, match.Length));
        return face;
    }
}
