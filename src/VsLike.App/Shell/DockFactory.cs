using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using VsLike.Core.Layout;

namespace VsLike.App.Shell;

/// <summary>
/// Baut das Dock-Layout aus einem <see cref="LayoutState"/> und liest es wieder aus.
/// Das Layout ist immer eine Funktion des Zustands; leere Bereiche werden weggelassen.
/// </summary>
public sealed class DockFactory : Factory
{
    private readonly IReadOnlyDictionary<string, PanelViewModel> _panels;

    public DockFactory(IEnumerable<PanelViewModel> panels)
    {
        _panels = panels.ToDictionary(p => p.Id, StringComparer.Ordinal);
        HideToolsOnClose = false;
    }

    public IReadOnlyCollection<PanelViewModel> Panels => [.. _panels.Values];

    public IDocumentDock? DocumentDock { get; private set; }

    public IRootDock Build(LayoutState state, IReadOnlyList<IDockable> documents, IDockable? activeDocument)
    {
        var placements = Normalize(state);
        var proportions = LayoutAreas.ToolAreas.ToDictionary(a => a, a => state.Proportions.GetValueOrDefault(a, LayoutAreas.DefaultProportions[a]));

        var documentDock = new DocumentDock
        {
            Id = LayoutAreas.Documents,
            IsCollapsable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>([.. documents]),
            ActiveDockable = activeDocument ?? documents.FirstOrDefault(),
        };
        DocumentDock = documentDock;

        var left = CreateToolDock(LayoutAreas.Left, placements, proportions, Alignment.Left);
        var right = CreateToolDock(LayoutAreas.Right, placements, proportions, Alignment.Right);
        var bottom = CreateToolDock(LayoutAreas.Bottom, placements, proportions, Alignment.Bottom);

        var centerItems = new List<IDockable> { documentDock };
        if (bottom is not null)
        {
            centerItems.Add(new ProportionalDockSplitter());
            centerItems.Add(bottom);
        }

        var center = new ProportionalDock
        {
            Id = "center",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList([.. centerItems]),
        };

        var mainItems = new List<IDockable>();
        if (left is not null)
        {
            mainItems.Add(left);
            mainItems.Add(new ProportionalDockSplitter());
        }

        mainItems.Add(center);
        if (right is not null)
        {
            mainItems.Add(new ProportionalDockSplitter());
            mainItems.Add(right);
        }

        var main = new ProportionalDock
        {
            Id = "main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList([.. mainItems]),
        };

        var root = CreateRootDock();
        root.Id = "root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        return root;
    }

    /// <summary>Reads the current placement of all panels from the live layout.</summary>
    public LayoutState Capture(IRootDock root, LayoutState previous)
    {
        var placements = new List<PanelPlacement>();
        var proportions = new Dictionary<string, double>(previous.Proportions);
        var previousById = Normalize(previous).ToDictionary(p => p.PanelId, StringComparer.Ordinal);

        foreach (var toolDock in Descendants(root).OfType<IToolDock>())
        {
            var area = ResolveArea(toolDock);
            if (LayoutAreas.ToolAreas.Contains(toolDock.Id) && !double.IsNaN(toolDock.Proportion))
            {
                proportions[toolDock.Id] = toolDock.Proportion;
            }

            var order = placements.Count(p => p.Area == area);
            foreach (var panel in toolDock.VisibleDockables?.OfType<PanelViewModel>() ?? [])
            {
                placements.Add(new PanelPlacement(panel.Id, area, order++, IsVisible: true, IsActive: ReferenceEquals(toolDock.ActiveDockable, panel)));
            }
        }

        foreach (var panel in _panels.Values.Where(p => placements.All(x => x.PanelId != p.Id)))
        {
            var last = previousById.GetValueOrDefault(panel.Id);
            placements.Add(new PanelPlacement(panel.Id, last?.Area ?? panel.Descriptor.DefaultArea, last?.Order ?? int.MaxValue, IsVisible: false, IsActive: false));
        }

        return previous with { Panels = placements, Proportions = proportions };
    }

    /// <summary>Walks the visible layout tree once, depth first.</summary>
    internal static IEnumerable<IDockable> Descendants(IDockable dockable)
    {
        yield return dockable;
        if (dockable is IDock { VisibleDockables: { } children })
        {
            foreach (var child in children.SelectMany(Descendants))
            {
                yield return child;
            }
        }
    }

    /// <summary>Fills in panels that the state does not know yet (e.g. new panels after an update).</summary>
    internal IReadOnlyList<PanelPlacement> Normalize(LayoutState state)
    {
        var known = state.Panels.Where(p => _panels.ContainsKey(p.PanelId) && LayoutAreas.ToolAreas.Contains(p.Area)).ToList();
        var missing = _panels.Values
            .Where(p => known.All(k => k.PanelId != p.Id))
            .Select(p => new PanelPlacement(p.Id, p.Descriptor.DefaultArea, int.MaxValue, IsVisible: true, IsActive: false));
        return [.. known, .. missing];
    }

    private static string ResolveArea(IDock dock)
    {
        for (IDockable? current = dock; current is not null; current = current.Owner)
        {
            if (LayoutAreas.ToolAreas.Contains(current.Id))
            {
                return current.Id;
            }
        }

        // Frei gezogene Docks: Ausrichtung entscheidet über den Bereich.
        return (dock as IToolDock)?.Alignment switch
        {
            Alignment.Right => LayoutAreas.Right,
            Alignment.Bottom or Alignment.Top => LayoutAreas.Bottom,
            _ => LayoutAreas.Left,
        };
    }

    private ToolDock? CreateToolDock(string area, IReadOnlyList<PanelPlacement> placements, IReadOnlyDictionary<string, double> proportions, Alignment alignment)
    {
        var inArea = placements.Where(p => p.Area == area && p.IsVisible).OrderBy(p => p.Order).ToList();
        if (inArea.Count == 0)
        {
            return null;
        }

        var tools = inArea.Select(p => (IDockable)_panels[p.PanelId]).ToArray();
        var activeId = inArea.FirstOrDefault(p => p.IsActive)?.PanelId ?? inArea[0].PanelId;
        return new ToolDock
        {
            Id = area,
            Alignment = alignment,
            Proportion = proportions[area],
            VisibleDockables = CreateList(tools),
            ActiveDockable = _panels[activeId],
        };
    }
}
