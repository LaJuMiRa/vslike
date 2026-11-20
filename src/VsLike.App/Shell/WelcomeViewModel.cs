using Dock.Model.Mvvm.Controls;
using VsLike.Core.Localization;

namespace VsLike.App.Shell;

public sealed class WelcomeViewModel : Document
{
    public WelcomeViewModel(string paletteChord)
    {
        Id = "document.welcome";
        PaletteChord = paletteChord;
        Title = Loc.Get("Welcome.Title");
        Loc.CultureChanged += (_, _) => Title = Loc.Get("Welcome.Title");
    }

    public string PaletteChord { get; }
}
