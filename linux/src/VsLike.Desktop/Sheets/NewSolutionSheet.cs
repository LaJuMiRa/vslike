using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Creates a solution with a first project: name, location, template (NewSolutionSheet in SimpleSheets.swift).</summary>
public sealed class NewSolutionSheet : SheetView
{
    private readonly Workspace _workspace;
    private readonly TextBox _name = new() { Text = "MyApp" };
    private readonly TemplateGrid _grid = new("console");
    private readonly TextBlock _error = Ui.Text(string.Empty, 11, "VsError");
    private readonly Control _working = Ui.HStack(6, Ui.Spinner(14));
    private readonly CheckBox _withTests = new() { Content = L.T("Add a test project (xUnit)") };
    private readonly Button _create;
    private readonly TextBlock _location;

    public NewSolutionSheet(Workspace workspace, Action dismiss)
        : base(dismiss)
    {
        _workspace = workspace;
        _location = Ui.Text(string.Empty, 13, "TextSecondary");
        ShowLocation();
        _working.IsVisible = false;
        ((StackPanel)_working).Children.Add(Ui.Text(L.T("Creating…"), 13, "TextSecondary"));
        _error.TextWrapping = TextWrapping.Wrap;
        _error.MaxHeight = 60;

        var choose = Ui.PushButton(L.T("Choose…"), () => _ = ChooseFolder());
        ToolTip.SetTip(choose, L.T("The folder is remembered for new solutions"));
        var form = Form.Section(
            null,
            Form.Labeled(L.T("Name"), _name),
            Form.Labeled(L.T("Location"), Ui.HStack(8, _location, choose)),
            _withTests);
        _name.MinWidth = 280;
        _name.TextChanged += (_, _) => UpdateEnabled();

        _create = Ui.PushButton(L.T("Create"), () => _ = Create(), prominent: true);
        var buttons = Buttons(_working, L.T("Create"), () => _ = Create(), dismiss, confirmButton: _create);
        var stack = Ui.VStack(16, Ui.Text(L.T("New Solution"), 20, "TextPrimary", FontWeight.Bold), form, _grid, _error, buttons);
        stack.Margin = new Thickness(22);
        stack.Width = 640;
        Content = stack;
        UpdateEnabled();
        _ = LoadTemplates();
    }

    protected override Control? FirstFocus() => _name;

    private void ShowLocation()
    {
        var folder = _workspace.Preferences.ProjectsFolder;
        var home = AppPaths.Home;
        _location.Text = folder.StartsWith(home, StringComparison.Ordinal) ? "~" + folder[home.Length..] : folder;
        _location.MaxWidth = 260;
        _location.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
    }

    private void UpdateEnabled() => _create.IsEnabled = !string.IsNullOrWhiteSpace(_name.Text) && !_working.IsVisible;

    private async Task ChooseFolder()
    {
        if (TopLevel.GetTopLevel(this) is { } owner
            && await OpenPanel.ChooseFolder(owner, L.T("Choose where the solution folder is created")) is { } folder)
        {
            _workspace.Preferences.ProjectsFolder = folder;
            ShowLocation();
        }
    }

    private async Task LoadTemplates()
    {
        IReadOnlyList<ProjectTemplate> found = [];
        try
        {
            found = await new ProjectService(CommandRunner.Dotnet()).Templates();
        }
        catch (CommandException)
        {
            // Without a usable dotnet the built-in list is shown; creating reports the real error.
        }

        _grid.SetTemplates(found.Count == 0 ? TemplateGrid.Fallback : found);
    }

    private async Task Create()
    {
        _working.IsVisible = true;
        UpdateEnabled();
        try
        {
            var name = (_name.Text ?? string.Empty).Trim();
            var service = new ProjectService(CommandRunner.Dotnet());
            var folder = System.IO.Path.Combine(_workspace.Preferences.ProjectsFolder, name);
            var solution = await service.CreateSolution(folder, name);
            var project = await service.CreateProject(_grid.Selection, name, folder);
            await service.AddProject(project, solution);
            if (_withTests.IsChecked == true)
            {
                var tests = await service.CreateProject("xunit", name + ".Tests", folder);
                await service.AddProject(tests, solution);
                await service.AddReference(project, tests);
            }

            try
            {
                await service.Runner.Run(["restore", solution]);
            }
            catch (CommandException)
            {
                // A failed restore shows up as problems once the solution is open.
            }

            Dismiss();
            await _workspace.Open(solution);
        }
        catch (Exception ex) when (ex is CommandException or IOException)
        {
            _error.Text = ex.Message;
            _working.IsVisible = false;
            UpdateEnabled();
        }
    }
}

/// <summary>Adds a project to the open solution (NewProjectSheet).</summary>
public sealed class NewProjectSheet : SheetView
{
    private readonly Workspace _workspace;
    private readonly SolutionModel _solution;
    private readonly TextBox _name = new() { Text = "MyLibrary" };
    private readonly TemplateGrid _grid = new("classlib");
    private readonly TextBlock _error = Ui.Text(string.Empty, 11, "VsError");
    private readonly Control _working = Ui.Spinner(14);
    private readonly Button _add;

    public NewProjectSheet(Workspace workspace, SolutionModel solution, Action dismiss)
        : base(dismiss)
    {
        _workspace = workspace;
        _solution = solution;
        _working.IsVisible = false;
        _error.TextWrapping = TextWrapping.Wrap;
        _add = Ui.PushButton(L.T("Add"), () => _ = Add(), prominent: true);
        _name.TextChanged += (_, _) => _add.IsEnabled = !string.IsNullOrEmpty(_name.Text) && !_working.IsVisible;
        var buttons = Buttons(_working, L.T("Add"), () => _ = Add(), dismiss, confirmButton: _add);
        var stack = Ui.VStack(16, Ui.Text(L.T("Add Project to %@", solution.Name), 20, "TextPrimary", FontWeight.Bold), _name, _grid, _error, buttons);
        stack.Margin = new Thickness(22);
        stack.Width = 640;
        Content = stack;
        _ = LoadTemplates();
    }

    protected override Control? FirstFocus() => _name;

    private async Task LoadTemplates()
    {
        IReadOnlyList<ProjectTemplate> found = [];
        try
        {
            found = await new ProjectService(CommandRunner.Dotnet()).Templates();
        }
        catch (CommandException)
        {
            // The built-in list is shown instead.
        }

        _grid.SetTemplates(found.Count == 0 ? TemplateGrid.Fallback : found);
    }

    private async Task Add()
    {
        _working.IsVisible = true;
        _add.IsEnabled = false;
        try
        {
            var service = new ProjectService(CommandRunner.Dotnet());
            var project = await service.CreateProject(_grid.Selection, _name.Text ?? string.Empty, _solution.Directory);
            await service.AddProject(project, _solution.FilePath);
            Dismiss();
            _workspace.RefreshTree();
            await _workspace.RestartEngine();
            _workspace.Tests.ReloadProjects();
        }
        catch (Exception ex) when (ex is CommandException or IOException)
        {
            _error.Text = ex.Message;
            _working.IsVisible = false;
            _add.IsEnabled = true;
        }
    }
}
