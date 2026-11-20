namespace VsLike.Core.Layout;

/// <param name="PanelId">Stable id of the tool panel, e.g. "panel.explorer".</param>
/// <param name="Area">Dock area id: "left", "right" or "bottom".</param>
/// <param name="Order">Tab order inside the area.</param>
/// <param name="IsVisible">False when the user closed the panel.</param>
/// <param name="IsActive">True for the selected tab of the area.</param>
public sealed record PanelPlacement(string PanelId, string Area, int Order, bool IsVisible, bool IsActive);
