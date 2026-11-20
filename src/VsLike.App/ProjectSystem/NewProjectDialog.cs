using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using VsLike.App.Localization;
using VsLike.Core.Documents;
using VsLike.Core.Localization;
using VsLike.Projects;
using VsLike.Projects.Templates;

namespace VsLike.App.ProjectSystem;

/// <summary>Dialog für neue Solution bzw. neues Projekt (im Code aufgebaut, bindet an <see cref="NewProjectViewModel"/>).</summary>
public sealed class NewProjectDialog : Window
{
    private NewProjectDialog(NewProjectViewModel vm, ProjectService projects, string? existingSolution, IDialogService dialogs)
    {
        DataContext = vm;
        Title = vm.Title;
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var form = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        form.Children.Add(new TextBlock { Text = vm.Title, FontSize = 18, FontWeight = FontWeight.SemiBold });
        form.Children.Add(Field("NewProject.Name", new TextBox { [!TextBox.TextProperty] = new Binding(nameof(vm.ProjectName)) }));
        if (vm.IsNewSolution)
        {
            form.Children.Add(Field("NewProject.SolutionName", new TextBox { [!TextBox.TextProperty] = new Binding(nameof(vm.SolutionName)) }));
            var browse = new Button { Content = Loc.Get("NewProject.Browse") };
            browse.Click += async (_, _) =>
            {
                if (await dialogs.PickFolderAsync("NewProject.Location").ConfigureAwait(true) is { } folder)
                {
                    vm.Location = folder;
                }
            };
            var location = new DockPanel { Children = { browse, new TextBox { [!TextBox.TextProperty] = new Binding(nameof(vm.Location)), Margin = new Thickness(0, 0, 8, 0) } } };
            DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
            form.Children.Add(Field("NewProject.Location", location));
            form.Children.Add(Field("NewProject.Format", new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Children =
                {
                    new RadioButton { Content = ".slnx", GroupName = "format", [!RadioButton.IsCheckedProperty] = new Binding(nameof(vm.UseSlnx)) },
                    new RadioButton { Content = ".sln", GroupName = "format", IsChecked = !vm.UseSlnx },
                },
            }));
        }
        else
        {
            form.Children.Add(Field("NewProject.Folder", new TextBox { [!TextBox.TextProperty] = new Binding(nameof(vm.SolutionFolder)) }));
        }

        form.Children.Add(Field("NewProject.Framework", new ComboBox { ItemsSource = vm.Frameworks, [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.Framework)) }));
        var templates = new ListBox
        {
            ItemsSource = vm.Templates,
            Height = 180,
            [!ListBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedTemplate)),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectTemplate>((t, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = t?.Name },
                    new TextBlock { Text = t?.ShortName, Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                },
            }),
        };
        form.Children.Add(Field("NewProject.Template", templates));
        form.Children.Add(new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(vm.Error)), Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap });

        var create = new Button { Content = Loc.Get("NewProject.Create"), IsDefault = true, Classes = { "accent" }, MinWidth = 100, [!IsEnabledProperty] = new Binding(nameof(vm.CanCreate)) };
        var cancel = new Button { Content = Loc.Get("Dialog.Cancel"), IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close(false);
        create.Click += async (_, _) =>
        {
            if (await vm.CreateAsync(projects, existingSolution, CancellationToken.None).ConfigureAwait(true))
            {
                Close(true);
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 20), Children = { cancel, create } };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel { Children = { buttons, new ScrollViewer { Content = form } } };
    }

    public static async Task<NewProjectViewModel?> ShowAsync(Window owner, Workbench workbench, bool newSolution, string? existingSolution)
    {
        var templates = await TemplateCatalog.ListAsync(workbench.Cli, CancellationToken.None).ConfigureAwait(true);
        var location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");
        var vm = new NewProjectViewModel(newSolution, location, templates);
        var created = await new NewProjectDialog(vm, workbench.ProjectService, existingSolution, workbench.Dialogs).ShowDialog<bool>(owner).ConfigureAwait(true);
        return created ? vm : null;
    }

    private static Control Field(string labelKey, Control input)
        => new StackPanel { Spacing = 4, Children = { new TextBlock { [!TextBlock.TextProperty] = new LocExtension(labelKey).ProvideValue(null!) as IBinding ?? throw new InvalidOperationException(), Opacity = 0.75 }, input } };
}
