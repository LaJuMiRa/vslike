using Dock.Model.Controls;
using VsLike.App.Shell;
using VsLike.Core.Layout;

namespace VsLike.App.Tests;

public class DockFactoryTests
{
    [Fact]
    public void Build_WithDefaultState_PlacesPanelsInDefaultAreas()
    {
        var factory = CreateFactory();

        var root = factory.Build(new LayoutState(), [], null);
        var state = factory.Capture(root, new LayoutState());

        Assert.Equal("left", Area(state, "explorer"));
        Assert.Equal("bottom", Area(state, "problems"));
        Assert.Equal("bottom", Area(state, "output"));
        Assert.Equal("right", Area(state, "properties"));
        Assert.All(state.Panels, p => Assert.True(p.IsVisible));
    }

    [Fact]
    public void BuildThenCapture_RoundTripsMovedAndHiddenPanels()
    {
        var factory = CreateFactory();
        var input = new LayoutState
        {
            Panels =
            [
                new PanelPlacement("output", "right", 0, true, true),
                new PanelPlacement("properties", "right", 1, true, false),
                new PanelPlacement("explorer", "left", 0, false, false),
            ],
            Proportions = new Dictionary<string, double> { ["right"] = 0.4 },
        };

        var state = factory.Capture(factory.Build(input, [], null), input);

        Assert.Equal("right", Area(state, "output"));
        Assert.False(state.Panels.Single(p => p.PanelId == "explorer").IsVisible);
        Assert.True(state.Panels.Single(p => p.PanelId == "output").IsActive);
        Assert.Equal(0.4, state.Proportions["right"]);
    }

    [Fact]
    public void Build_OmitsEmptyAreas()
    {
        var factory = CreateFactory();
        var input = new LayoutState { Panels = [new PanelPlacement("explorer", "left", 0, false, false)] };

        var root = factory.Build(input, [], null);

        Assert.DoesNotContain(DockFactory.Descendants(root).OfType<IToolDock>(), d => d.Id == "left");
    }

    [Fact]
    public void Normalize_AddsUnknownPanelsAndDropsStaleOnes()
    {
        var factory = CreateFactory();
        var input = new LayoutState { Panels = [new PanelPlacement("removed-panel", "left", 0, true, true)] };

        var placements = factory.Normalize(input);

        Assert.DoesNotContain(placements, p => p.PanelId == "removed-panel");
        Assert.Equal(4, placements.Count);
    }

    private static string Area(LayoutState state, string panelId) => state.Panels.Single(p => p.PanelId == panelId).Area;

    private static DockFactory CreateFactory()
        => new(ShellPanels.CreateDefault().Select(p => new PanelViewModel(p.Descriptor, p.Content)));
}
