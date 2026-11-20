namespace VsLike.App.Shell;

/// <summary>Die Panel-Definitionen der Shell.</summary>
public static class ShellPanels
{
    public static readonly PanelDescriptor Explorer = new("explorer", "Panel.Explorer", LayoutAreas.Left, "Cmd+Shift+E");
    public static readonly PanelDescriptor Problems = new("problems", "Panel.Errors", LayoutAreas.Bottom, "Cmd+Shift+M");
    public static readonly PanelDescriptor Output = new("output", "Panel.Output", LayoutAreas.Bottom, "Cmd+Shift+U");
    public static readonly PanelDescriptor Search = new("search", "Panel.Search", LayoutAreas.Bottom);
    public static readonly PanelDescriptor Properties = new("properties", "Panel.Properties", LayoutAreas.Right);

    /// <summary>Panels with placeholder content (used by layout tests).</summary>
    public static IReadOnlyList<(PanelDescriptor Descriptor, object Content)> CreateDefault() =>
    [
        (Explorer, new EmptyPanelViewModel()),
        (Problems, new EmptyPanelViewModel()),
        (Output, new EmptyPanelViewModel()),
        (Search, new EmptyPanelViewModel()),
        (Properties, new EmptyPanelViewModel()),
    ];
}
