using VsLike.Projects.Build;

namespace VsLike.App.Build;

/// <summary>Eintrag der Profilauswahl; <see cref="Profile"/> ist null für „ohne Startprofil“.</summary>
public sealed record LaunchProfileItem(string Name, LaunchProfile? Profile)
{
    public override string ToString() => Name;
}
