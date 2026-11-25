using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using VsLike.Desktop.Editor;

namespace VsLike.Desktop;

/// <summary>Terminal tabs (login shells and Run sessions) above the active terminal (TerminalPanel in TerminalAndDebug.swift).</summary>
public sealed class TerminalPanel : ReactiveView
{
    // The terminal view of a session lives as long as the session, so switching panel tabs keeps its screen and scrollback.
    private static readonly ConditionalWeakTable<TerminalSession, TerminalHost> Hosts = [];

    private readonly Workspace _workspace;
    private bool _started;

    public TerminalPanel(Workspace workspace)
    {
        _workspace = workspace;
    }

    protected override IEnumerable<INotifyPropertyChanged?> Observed()
    {
        var terminals = _workspace.Terminals;
        return [terminals, .. terminals.Sessions];
    }

    protected override bool IsRelevant(object? sender, string? property) =>
        sender is TerminalSession
            ? property is nameof(TerminalSession.Title) or nameof(TerminalSession.IsRunning) or nameof(TerminalSession.ExitCode)
            : property is nameof(TerminalController.Sessions) or nameof(TerminalController.ActiveId);

    protected override Control Render()
    {
        var terminals = _workspace.Terminals;
        if (terminals.Sessions.Count == 0)
        {
            if (!_started)
            {
                _started = true;
                Dispatcher.UIThread.Post(() => _ = terminals.NewShell());
            }

            var button = Ui.PushButton(L.T("New Terminal"), () => _ = terminals.NewShell(), prominent: true);
            return WithBackground(Ui.Unavailable(L.T("No terminal"), "terminal", L.T("Run starts your program here, so it can read input."), button));
        }

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 4) };
        foreach (var session in terminals.Sessions)
        {
            strip.Children.Add(Tab(session, session.Id == terminals.ActiveId));
        }

        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        dock.Children.Add(strip);
        if (terminals.Active is { } active)
        {
            var holder = Hosts.GetValue(active, s => new TerminalHost(s, terminals.TerminalFontSize)).Mount();
            holder.Margin = new Thickness(6, 0, 0, 0);
            dock.Children.Add(holder);
        }

        return WithBackground(dock);
    }

    private static Control WithBackground(Control content)
    {
        var panel = new Border { Child = content };
        panel.Background = Palette.EditorBackground.Brush;
        return panel;
    }

    private Control Tab(TerminalSession session, bool isActive)
    {
        var row = Ui.HStack(
            5,
            Icons.Make(session.IsRun ? "play.fill" : "terminal", 9, session.IsRun ? "VsAccent" : "TextSecondary"),
            Ui.Text(session.Title, 11));
        if (!session.IsRunning)
        {
            var code = session.ExitCode is { } value ? string.Create(CultureInfo.InvariantCulture, $"({value})") : string.Empty;
            row.Children.Add(Ui.Text(code, 11, session.ExitCode == 0 ? "VsSuccess" : "VsError"));
        }

        row.Children.Add(Ui.Plain(Icons.Make("xmark", 8, "TextSecondary", 2.6), () => _workspace.Terminals.Close(session)));
        var face = new Border { Child = row, Height = 22, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
        if (isActive)
        {
            Ui.Res(face, Border.BackgroundProperty, "FillSelected");
        }

        Ui.OnTap(face, () => _workspace.Terminals.ActiveId = session.Id);
        return face;
    }

    /// <summary>The terminal control of one session and the border it is mounted in.</summary>
    private sealed class TerminalHost
    {
        private readonly TerminalControl _terminal;
        private readonly TerminalSession _session;
        private bool _attached;

        public TerminalHost(TerminalSession session, double fontSize)
        {
            _session = session;
            _terminal = new TerminalControl
            {
                FontFamily = Ui.Mono,
                FontSize = fontSize,
            };
            ApplyColors();
            _terminal.TemplateApplied += (_, _) => Attach();
            if (Application.Current is { } application)
            {
                application.ActualThemeVariantChanged += (_, _) => ApplyColors();
            }
        }

        /// <summary>Returns a border holding the terminal (the control is taken out of the previous one).</summary>
        public Control Mount()
        {
            if (_terminal.Parent is Border previous)
            {
                previous.Child = null;
            }

            var holder = new Border { Child = _terminal };
            Dispatcher.UIThread.Post(() => _terminal.Focus());
            return holder;
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _terminal.AttachConnection(_session.Connection);
        }

        private void ApplyColors()
        {
            var dark = Palette.IsDark;
            var background = Palette.Hex(dark ? TerminalSession.BackgroundDark : TerminalSession.BackgroundLight);
            var foreground = Palette.Hex(dark ? TerminalSession.ForegroundDark : TerminalSession.ForegroundLight);
            _terminal.Background = new SolidColorBrush(background);
            _terminal.Foreground = new SolidColorBrush(foreground);
            _terminal.CursorColor = foreground;
            _terminal.SelectionBrush = Palette.Selection.Brush;
        }
    }
}
