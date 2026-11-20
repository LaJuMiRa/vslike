using Dock.Model.Mvvm.Controls;
using VsLike.Core.Localization;

namespace VsLike.App.Shell;

/// <summary>
/// Andockbares Werkzeugfenster. <see cref="Content"/> ist das ViewModel des eigentlichen Panels.
/// </summary>
public sealed class PanelViewModel : Tool
{
    public PanelViewModel(PanelDescriptor descriptor, object content)
    {
        Descriptor = descriptor;
        Content = content;
        Id = descriptor.Id;
        Title = Loc.Get(descriptor.TitleKey);
        CanFloat = false;
        Loc.CultureChanged += (_, _) => Title = Loc.Get(descriptor.TitleKey);
    }

    public PanelDescriptor Descriptor { get; }

    /// <summary>Gets the panel's view model; set once during composition for panels created after the shell.</summary>
    public object Content { get; internal set; }
}
