using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using VsLike.App.Editor;
using VsLike.App.Explorer;
using VsLike.App.Localization;
using VsLike.App.Problems;
using VsLike.App.Search;

namespace VsLike.App.Shell;

/// <summary>
/// Ordnet ViewModels ihre Ansichten zu und baut dabei immer ein neues Steuerelement. Bewusst kein
/// <see cref="IRecyclingDataTemplate"/>: Avalonias XAML-DataTemplate gibt ein übergebenes Steuerelement
/// unverändert zurück, und Dock reicht beim Tab-Wechsel das Steuerelement des vorherigen Tabs weiter.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<object, Control>> Factories = new()
    {
        [typeof(EditorDocumentViewModel)] = _ => new EditorView(),
        [typeof(ExplorerViewModel)] = _ => new ExplorerView(),
        [typeof(FindInFilesViewModel)] = _ => new FindInFilesView(),
        [typeof(ProblemsViewModel)] = _ => new ProblemsView(),
        [typeof(WelcomeViewModel)] = _ => new WelcomeView(),
        [typeof(PanelViewModel)] = data => new ContentControl { Content = ((PanelViewModel)data).Content },
        [typeof(EmptyPanelViewModel)] = _ => new TextBlock
        {
            Classes = { "muted" },
            [!TextBlock.TextProperty] = new LocExtension("Panel.Empty").ProvideValue(null!) as Avalonia.Data.Binding ?? throw new InvalidOperationException(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    public static void Register<TViewModel>(Func<TViewModel, Control> factory)
        => Factories[typeof(TViewModel)] = data => factory((TViewModel)data);

    public bool Match(object? data) => data is not null && Factories.ContainsKey(data.GetType());

    public Control? Build(object? param)
        => param is not null && Factories.TryGetValue(param.GetType(), out var factory) ? Attach(factory(param), param) : null;

    private static Control Attach(Control control, object dataContext)
    {
        control.DataContext = dataContext;
        return control;
    }
}
