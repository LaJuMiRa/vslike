using VsLike.Diagram.Export;
using VsLike.Diagram.Layout;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Tests;

public sealed class LayoutTests
{
    private static readonly DiagramModel Model = new(
        [
            new DiagramType("Shop.Entity", "Entity", "Shop", DiagramTypeKind.Class, Modifiers: TypeModifiers.Abstract, Members: [new DiagramMember(MemberKind.Property, "Id", "int")]),
            new DiagramType("Shop.Order", "Order", "Shop", DiagramTypeKind.Class, BaseType: "Entity", Members:
            [
                new DiagramMember(MemberKind.Field, "_items", "List<Item>", Access.Private),
                new DiagramMember(MemberKind.Method, "Total", "decimal", Parameters: [new DiagramParameter("withTax", "bool")]),
            ]),
            new DiagramType("Shop.Item", "Item", "Shop", DiagramTypeKind.Record),
            new DiagramType("System.IDisposable", "IDisposable", "System", DiagramTypeKind.Interface, IsExternal: true),
        ],
        [
            new DiagramRelation("Shop.Order", "Shop.Entity", RelationKind.Inheritance),
            new DiagramRelation("Shop.Order", "System.IDisposable", RelationKind.Implementation),
            new DiagramRelation("Shop.Order", "Shop.Item", RelationKind.Composition, "_items", IsMany: true),
        ]);

    [Fact]
    public void Lines_UseUmlNotation()
    {
        Assert.Equal("- _items: List<Item>", BoxMetrics.Line(Model.Types[1].MemberList[0]));
        Assert.Equal("+ Total(withTax: bool): decimal", BoxMetrics.Line(Model.Types[1].MemberList[1]));
        Assert.Equal("«abstract»", BoxMetrics.Stereotype(Model.Types[0]));
        Assert.Empty(BoxMetrics.Lines(Model.Types[3], collapsed: false));
    }

    [Fact]
    public void AutoLayout_PutsBaseTypesAboveDerivedAndKeepsExistingPositions()
    {
        var document = new DiagramDocument { Types = [.. Model.Types.Select(t => new TypePlacement(t.FullName, double.NaN, double.NaN))] };

        var arranged = AutoLayout.Arrange(document, Model, all: true);

        Assert.True(arranged.Find("Shop.Entity")!.Y < arranged.Find("Shop.Order")!.Y);
        Assert.True(arranged.Find("System.IDisposable")!.Y < arranged.Find("Shop.Order")!.Y);
        Assert.All(arranged.Types, t => Assert.Equal(0, t.X % BoxMetrics.GridSize));

        var moved = arranged.WithPlacement(new TypePlacement("Shop.Order", 600, 400)).WithPlacement(new TypePlacement("Shop.Item", double.NaN, double.NaN));
        var again = AutoLayout.Arrange(moved, Model, all: false);
        Assert.Equal((600d, 400d), (again.Find("Shop.Order")!.X, again.Find("Shop.Order")!.Y));
        Assert.False(double.IsNaN(again.Find("Shop.Item")!.X));
    }

    [Fact]
    public void Document_RoundTripsAndFollowsRenames()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + DiagramDocument.Extension);
        try
        {
            new DiagramDocument { Zoom = 1.5, Types = [new TypePlacement("Shop.Order", 20, 40, Collapsed: true)] }.Save(path);
            var loaded = DiagramDocument.Load(path).Renamed("Shop.Order", "Shop.Purchase");

            Assert.Equal(1.5, loaded.Zoom);
            Assert.Equal(new TypePlacement("Shop.Purchase", 20, 40, true), Assert.Single(loaded.Types));
            Assert.Equal(Path.GetFileName(path)[..^DiagramDocument.Extension.Length], DiagramDocument.NameOf(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public Task Svg_ExportIsStable()
    {
        var document = AutoLayout.Arrange(new DiagramDocument { Types = [.. Model.Types.Select(t => new TypePlacement(t.FullName, double.NaN, double.NaN))] }, Model, all: true);
        return Verify(SvgExporter.Export(Model, document), "svg").UseDirectory("Snapshots");
    }
}
