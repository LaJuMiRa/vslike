using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// The toolbar (port of WorkspaceToolbar.swift): run controls on the left, the target picker with the activity pill in the
/// middle, analysis and view toggles on the right. Debug controls replace the build button while a debug session runs.
/// Linux: it is also the window's title bar (drag to move, window buttons at the right end).
/// </summary>
public sealed class WorkspaceToolbar : UserControl
{
    public WorkspaceToolbar(Workspace workspace, MainWindow window, Action? showSidebar)
    {
        var leading = Ui.HStack(8);
        if (showSidebar is not null)
        {
            leading.Children.Add(TitleBar.MenuButton(window));
            leading.Children.Add(Ui.IconButton("sidebar.left", showSidebar, L.T("Show Sidebar"), 17, "TextSecondary", toolbar: true));
        }

        leading.Children.Add(Pill(new Live(() => RunControls(workspace), workspace, workspace.Debugger, workspace.Terminals, workspace.Build)));
        var center = ActivityBar(workspace);
        var trailing = Ui.HStack(0, Pill(new Live(() => AnalysisControls(workspace), workspace)), TitleBar.WindowButtons(window));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = TitleBar.Height, Margin = new Thickness(8, 0, 0, 0) };
        grid.Children.Add(leading);
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.Margin = new Thickness(12, 0);
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        Content = TitleBar.Draggable(grid, window);
        Ui.Res(this, BackgroundProperty, "WindowBackground");
    }

    /// <summary>The rounded group around toolbar buttons (the glass groups of the macOS 26 toolbar).</summary>
    internal static Border Pill(Control content)
    {
        var pill = new Border
        {
            Child = content,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(4, 0),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 1 3 0 #14000000"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Ui.Res(pill, Border.BackgroundProperty, "ControlBackground");
        Ui.Res(pill, Border.BorderBrushProperty, "Separator");
        return pill;
    }

    private static Control RunControls(Workspace workspace)
    {
        var debugger = workspace.Debugger;
        var isRunning = debugger.IsActive || workspace.Terminals.HasRunningProgram || workspace.Build.IsBusy;
        var run = Ui.IconButton("play.fill", () => _ = debugger.Start(), debugger.IsStopped ? L.T("Continue (⌘R)") : L.T("Run with debugger (⌘R)"), 15, "TextPrimary", toolbar: true);
        run.IsEnabled = workspace.Solution is not null && !(debugger.IsActive && !debugger.IsStopped) && !debugger.IsStarting;
        var stop = Ui.IconButton("stop.fill", () => Stop(workspace), L.T("Stop (⌘.)"), 15, "TextPrimary", toolbar: true);
        stop.IsEnabled = isRunning;
        var row = Ui.HStack(2, run, stop);

        if (debugger.IsActive)
        {
            var pause = Ui.IconButton("pause.fill", () => _ = debugger.Pause(), L.T("Pause"), 14, "TextPrimary", toolbar: true);
            pause.IsEnabled = !debugger.IsStopped;
            row.Children.Add(pause);
            foreach (var (symbol, tip, action) in new (string, string, Func<Task>)[]
            {
                ("arrow.turn.down.right", "Step Over (F10)", debugger.StepOver),
                ("arrow.down.to.line", "Step Into (F11)", debugger.StepIn),
                ("arrow.up.to.line", "Step Out (⇧F11)", debugger.StepOut),
            })
            {
                var button = Ui.IconButton(symbol, () => _ = action(), L.T(tip), 15, "TextPrimary", toolbar: true);
                button.IsEnabled = debugger.IsStopped;
                row.Children.Add(button);
            }

            return row;
        }

        var build = Ui.IconButton("hammer", () => workspace.Build.Start(BuildKind.Build), L.T("Build (⌘B)"), 16, "TextPrimary", toolbar: true);
        var more = Ui.IconButton("chevron.down", () => { }, null, 9, "TextSecondary", toolbar: true);
        more.Width = 20;
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => flyout.ItemsSource = Ui.Menu(
        [
            new MenuEntry(L.T("Build Solution"), () => workspace.Build.Start(BuildKind.Build), "hammer", "Ctrl+B"),
            new MenuEntry(L.T("Rebuild Solution"), () => workspace.Build.Start(BuildKind.Rebuild), "arrow.triangle.2.circlepath"),
            new MenuEntry(L.T("Clean Solution"), () => workspace.Build.Start(BuildKind.Clean), "trash"),
            null,
            new MenuEntry(L.T("Run Without Debugging"), workspace.Build.StartWithoutDebugging, "play"),
            new MenuEntry(L.T("Run Tests"), () => workspace.Tests.Run(), "checkmark.diamond"),
        ]);
        more.Flyout = flyout;
        var enabled = workspace.Solution is not null && !workspace.Build.IsBusy;
        build.IsEnabled = enabled;
        more.IsEnabled = enabled;
        row.Children.Add(build);
        row.Children.Add(Ui.Res(new Border { Width = 1, Height = 16, Margin = new Thickness(2, 0) }, Border.BackgroundProperty, "Separator"));
        row.Children.Add(more);
        return row;
    }

    private static void Stop(Workspace workspace)
    {
        _ = workspace.Debugger.Stop();
        workspace.Terminals.StopRuns();
        workspace.Build.Cancel();
    }

    /// <summary>Target, configuration and launch profile in one capsule, followed by the current activity.</summary>
    private static Control ActivityBar(Workspace workspace)
    {
        var target = new Live(() => TargetMenu(workspace), workspace.Build, workspace);
        var separator = Ui.Text("›", 13, "TextTertiary");
        separator.Margin = new Thickness(6, 0);
        var configuration = new Live(() => ConfigurationMenu(workspace), workspace.Build);
        var divider = Ui.Res(new Border { Width = 1, Height = 16, Margin = new Thickness(10, 0) }, Border.BackgroundProperty, "Separator");
        var status = new Live(() => StatusPill(workspace), workspace, workspace.Diagnostics);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*") };
        Control[] parts = [target, separator, configuration, divider, status];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(parts[i], i);
            grid.Children.Add(parts[i]);
        }

        status.Margin = new Thickness(0, 0, 12, 0);
        target.Margin = new Thickness(12, 0, 0, 0);
        var capsule = new Border
        {
            Child = grid,
            Height = 34,
            MinWidth = 300,
            MaxWidth = 560,
            Width = 560,
            CornerRadius = new CornerRadius(17),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Ui.Res(capsule, Border.BackgroundProperty, "FillCapsule");
        capsule.SizeChanged += (_, _) => { };
        return capsule;
    }

    private static Control TargetMenu(Workspace workspace)
    {
        var build = workspace.Build;
        var label = Ui.HStack(
            5,
            Icons.Make("shippingbox.fill", 14, "VsAccent"),
            Ui.Text(build.StartupProjectName ?? L.T("No target"), 13, "VsAccent"),
            build.LaunchProfile is { } profile && build.Profiles.Count > 1 ? Ui.Text(profile, 13, "TextSecondary") : null);
        var button = new Button { Content = label, Classes = { "plain" } };
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            var entries = new List<MenuEntry?>();
            if (build.RunnableProjects.Count == 0)
            {
                entries.Add(new MenuEntry(L.T("No startable project"), Enabled: false));
            }

            entries.AddRange(build.RunnableProjects.Select(p => new MenuEntry(p.Name, () => build.StartupProject = p.FilePath, Checked: build.StartupProject == p.FilePath)));
            if (build.Profiles.Count > 0)
            {
                entries.Add(null);
                entries.Add(new MenuEntry(L.T("Launch Profile"), Enabled: false));
                entries.AddRange(build.Profiles.Select(p => new MenuEntry(p.Name, () => build.LaunchProfile = p.Name, Checked: build.LaunchProfile == p.Name)));
            }

            flyout.ItemsSource = Ui.Menu(entries);
        };
        button.Flyout = flyout;
        return button;
    }

    private static Control ConfigurationMenu(Workspace workspace)
    {
        var build = workspace.Build;
        var button = new Button { Content = Ui.Text(build.Configuration, 13, "TextSecondary"), Classes = { "plain" } };
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => flyout.ItemsSource = Ui.Menu(BuildController.Configurations.Select(c => (MenuEntry?)new MenuEntry(c, () => build.Configuration = c)));
        button.Flyout = flyout;
        return button;
    }

    private static Control StatusPill(Workspace workspace)
    {
        var status = workspace.Status;
        Control? icon = status.Style switch
        {
            StatusStyle.Progress => Ui.Spinner(12),
            StatusStyle.Success => Icons.Make("checkmark.circle.fill", 14, "VsSuccess"),
            StatusStyle.Warning => Icons.Make("exclamationmark.triangle.fill", 14, "VsWarning"),
            StatusStyle.Error => Icons.Make("xmark.octagon.fill", 14, "VsError"),
            _ => null,
        };
        var text = Ui.Text(status.Text, 13, "TextPrimary", FontWeight.Medium);
        text.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        var errors = workspace.Diagnostics.ErrorCount;
        var warnings = workspace.Diagnostics.WarningCount;
        var counts = Ui.HStack(
            8,
            Ui.HStack(4, Icons.Make("xmark.octagon.fill", 12, errors > 0 ? "VsError" : "TextSecondary"), Ui.Text(Count(errors), 12, errors > 0 ? "VsError" : "TextSecondary")),
            Ui.HStack(4, Icons.Make("exclamationmark.triangle.fill", 12, warnings > 0 ? "VsWarning" : "TextSecondary"), Ui.Text(Count(warnings), 12, warnings > 0 ? "VsWarning" : "TextSecondary")));
        var problems = Ui.Plain(counts, () => workspace.Show(BottomTab.Problems), L.T("Show Problems (⇧⌘M)"));
        var leading = Ui.HStack(7, icon, text);
        var dock = new DockPanel();
        DockPanel.SetDock(problems, Dock.Right);
        problems.Margin = new Thickness(8, 0, 0, 0);
        dock.Children.Add(problems);
        dock.Children.Add(leading);
        return dock;
    }

    private static string Count(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Control AnalysisControls(Workspace workspace)
    {
        Control styleCopContent = workspace.IsStyleCopLoading
            ? Ui.Spinner(14)
            : Icons.Make(workspace.StyleCopEnabled ? "checkmark.seal.fill" : "checkmark.seal", 18, workspace.StyleCopEnabled ? "VsStyleCop" : "TextSecondary");
        var styleCop = new Button { Content = styleCopContent, Classes = { "borderless", "toolbar" } };
        styleCop.Click += (_, _) => _ = workspace.ToggleStyleCop();
        Ui.Tip(styleCop, workspace.StyleCopEnabled ? L.T("StyleCop is on for this solution") : L.T("StyleCop is off for this solution"));
        styleCop.IsEnabled = workspace.Solution is not null && !workspace.IsStyleCopLoading;
        Ui.ContextMenu(styleCop, () => [new MenuEntry(L.T("StyleCop Settings…"), () => _ = workspace.OpenStyleCopSettings())]);

        var diagram = Ui.IconButton("rectangle.3.group", () => _ = workspace.OpenSolutionDiagram(), L.T("Class Diagram (⇧⌘D)"), 17, "TextPrimary", toolbar: true);
        diagram.IsEnabled = workspace.Solution is not null;
        var terminal = Ui.IconButton("terminal", () => _ = workspace.ShowTerminal(), L.T("Terminal (⌃`)"), 17, "TextPrimary", toolbar: true);
        var panel = Ui.IconButton("rectangle.bottomthird.inset.filled", () => workspace.IsBottomPanelVisible = !workspace.IsBottomPanelVisible, L.T("Show or hide the panel (⌘J)"), 17, "TextPrimary", toolbar: true);
        var inspector = Ui.IconButton("sidebar.right", () => workspace.IsInspectorVisible = !workspace.IsInspectorVisible, L.T("Inspector"), 17, "TextPrimary", toolbar: true);
        return Ui.HStack(2, styleCop, diagram, terminal, panel, inspector);
    }
}
