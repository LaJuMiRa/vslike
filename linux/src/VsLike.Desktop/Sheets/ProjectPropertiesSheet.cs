using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>Project properties (TargetFramework, Nullable …) and project references (ProjectPropertiesSheet in ProjectSheets.swift).</summary>
public sealed class ProjectPropertiesSheet : SheetView
{
    private readonly Workspace _workspace;
    private readonly string _project;
    private readonly Dictionary<string, string> _values = [];
    private readonly Dictionary<string, string> _original = [];
    private readonly HashSet<string> _references = [];
    private readonly HashSet<string> _originalReferences = [];
    private readonly ContentControl _form = new();
    private readonly TextBlock _error = Ui.Text(string.Empty, 11, "VsError");
    private readonly Control _saving = Ui.Spinner(14);
    private IReadOnlyList<TargetFrameworkOption> _frameworks = [];

    public ProjectPropertiesSheet(Workspace workspace, string project, Action dismiss)
        : base(dismiss)
    {
        _workspace = workspace;
        _project = project;
        _saving.IsVisible = false;
        Load();
        var header = Ui.HStack(
            8,
            Icons.Make("slider.horizontal.3", 20, "VsAccent"),
            Ui.Text(System.IO.Path.GetFileNameWithoutExtension(project), 15, "TextPrimary", FontWeight.SemiBold));
        header.Margin = new Thickness(14);
        var buttons = Buttons(_saving, L.T("Save"), () => _ = Save(), dismiss);
        buttons.Margin = new Thickness(14);
        _error.Margin = new Thickness(14, 0);
        var dock = new DockPanel { Width = 560, Height = 620 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_error, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(buttons);
        dock.Children.Add(_error);
        dock.Children.Add(_form);
        Content = dock;
        Redraw();
        _ = LoadFrameworks();
    }

    private static readonly string[] NullableOptions = ["enable", "disable", "warnings", "annotations"];

    private void Load()
    {
        try
        {
            var file = ProjectFile.Load(_project);
            foreach (var key in ProjectFile.EditableProperties)
            {
                _values[key] = file.Property(key) ?? string.Empty;
                _original[key] = _values[key];
            }

            foreach (var reference in file.ProjectReferences())
            {
                _references.Add(reference);
                _originalReferences.Add(reference);
            }
        }
        catch (IOException ex)
        {
            _error.Text = ex.Message;
        }
    }

    private async Task LoadFrameworks()
    {
        try
        {
            _frameworks = DotnetLocator.TargetFrameworks(await DotnetLocator.ListSdks(CommandRunner.Dotnet()));
            Redraw();
        }
        catch (CommandException)
        {
            // The current value stays selectable.
        }
    }

    private void Redraw()
    {
        var properties = ProjectFile.EditableProperties.Select(PropertyField).ToArray();
        var candidates = (_workspace.Solution?.Projects ?? []).Where(p => p.FilePath != _project).ToList();
        var references = new List<Control?>();
        if (candidates.Count == 0)
        {
            references.Add(Ui.Text(L.T("The solution has no other projects."), 13, "TextSecondary"));
        }

        foreach (var other in candidates)
        {
            var path = other.FilePath;
            references.Add(Form.Toggle(other.Name, _references.Contains(path), on =>
            {
                if (on)
                {
                    _references.Add(path);
                }
                else
                {
                    _references.Remove(path);
                }
            }));
        }

        _form.Content = Form.Grouped(Form.Section(L.T("Properties"), properties), Form.Section(L.T("Project References"), [.. references]));
    }

    private Control PropertyField(string key)
    {
        void Set(string value) => _values[key] = value;
        var current = _values.GetValueOrDefault(key, string.Empty);
        switch (key)
        {
            case "TargetFramework":
                var options = _frameworks.Select(f => ($"{f.Moniker}  (.NET SDK {string.Join(", ", f.SdkVersions)})", f.Moniker)).ToList();
                if (current.Length > 0 && !_frameworks.Any(f => f.Moniker == current))
                {
                    options.Add((current, current));
                }

                return Form.Labeled(key, Form.Picker(options, current, Set));
            case "Nullable":
                return Form.Labeled(key, Form.Picker([("–", string.Empty), .. NullableOptions.Select(o => (o, o))], current, Set));
            case "OutputType":
                return Form.Labeled(key, Form.Picker([("–", string.Empty), ("Exe", "Exe"), ("Library", "Library"), ("WinExe", "WinExe")], current, Set));
            case "ImplicitUsings":
            case "TreatWarningsAsErrors":
            case "GenerateDocumentationFile":
            case "InvariantGlobalization":
                return Form.Labeled(key, Form.Picker([("–", string.Empty), ("true", "true"), ("false", "false"), ("enable", "enable")], current, Set));
            default:
                return Form.TextField(key, current, Set);
        }
    }

    private async Task Save()
    {
        _saving.IsVisible = true;
        try
        {
            if (!_values.SequenceEqual(_original))
            {
                var file = ProjectFile.Load(_project);
                foreach (var key in ProjectFile.EditableProperties.Where(k => _values[k] != _original[k]))
                {
                    file = file.SetProperty(key, _values[key]);
                }

                file.Save();
            }

            if (!_references.SetEquals(_originalReferences))
            {
                var service = new ProjectService(CommandRunner.Dotnet());
                foreach (var added in _references.Except(_originalReferences))
                {
                    await service.AddReference(added, _project);
                }

                foreach (var removed in _originalReferences.Except(_references))
                {
                    await service.RemoveReference(removed, _project);
                }
            }

            Dismiss();
            _workspace.RefreshTree();
            await _workspace.RestartEngine();
        }
        catch (Exception ex) when (ex is CommandException or IOException)
        {
            _error.Text = ex.Message;
            _saving.IsVisible = false;
        }
    }
}
