namespace VsLike.App.Shell;

/// <param name="Id">Stable id, used in layout.json and command ids.</param>
/// <param name="TitleKey">Resource key of the tab title.</param>
/// <param name="DefaultArea">"left", "right" or "bottom".</param>
/// <param name="ToggleChord">Optional default shortcut for showing/hiding the panel.</param>
public sealed record PanelDescriptor(string Id, string TitleKey, string DefaultArea, string? ToggleChord = null);
