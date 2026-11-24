using System.Text.Json;
using VsLike.Kit;
using Xunit;

namespace VsLike.Kit.Tests;

public sealed class DiagramTests
{
    private const string Json = """
        {"types":[
          {"fullName":"Shop.IVehicle","name":"IVehicle","namespace":"Shop","kind":"interface","access":"public","modifiers":0,
           "members":[{"kind":"method","name":"Drive","type":"string","access":"public","modifiers":0,"accessors":"getSet","signature":"Drive()"}],
           "filePaths":["/s/IVehicle.cs"],"isExternal":false},
          {"fullName":"Shop.Car","name":"Car","namespace":"Shop","kind":"class","access":"public","modifiers":5,
           "typeParameters":[{"name":"T","constraints":"class"}],"interfaces":["IVehicle"],
           "members":[{"kind":"property","name":"Name","type":"string","access":"public","modifiers":0,"accessors":"getInit"},
                      {"kind":"method","name":"Honk","type":"void","access":"private","modifiers":1,"accessors":"getSet","parameters":[{"name":"times","type":"int"}]}],
           "filePaths":["/s/Car.cs"]},
          {"fullName":"System.IDisposable","name":"IDisposable","namespace":"System","kind":"interface","isExternal":true}
        ],"relations":[{"from":"Shop.Car","to":"Shop.IVehicle","kind":"implementation"},{"from":"Shop.Car","to":"System.IDisposable","kind":"implementation","label":"x","isMany":true}]}
        """;

    private static DiagramModel Model() => JsonSerializer.Deserialize<DiagramModel>(Json, JSONValue.Options)!;

    private static DiagramDocument Unplaced(DiagramModel model) => new([.. model.Types.Select(t => new TypePlacement(t.FullName))]);

    [Fact]
    public void DecodesEngineJsonWithOptionalFieldsAndFlags()
    {
        var model = Model();
        var car = Assert.IsType<DiagramType>(model.Find("Shop.Car"));

        Assert.Equal(TypeModifiers.Abstract | TypeModifiers.Sealed, car.Modifiers);
        Assert.Equal("Car<T>", car.DisplayName);
        Assert.Equal("«abstract»", car.Stereotype);
        Assert.Equal("- Honk(times: int): void", car.Members[1].UmlLine);
        Assert.Equal("Honk(int)", car.Members[1].Signature);
        Assert.Equal(PropertyAccessors.GetInit, car.Members[0].Accessors);
        Assert.Equal("«interface»", model.Find("System.IDisposable")?.Stereotype);
        Assert.True(model.Relations[1].IsMany);
    }

    [Fact]
    public void MemberEncodesWithoutComputedFields()
    {
        var member = new DiagramMember(MemberKind.Method, "Run", "void", parameters: [new DiagramParameter("x", "int")]);
        var json = JsonSerializer.Serialize(member, JSONValue.Options);

        Assert.Contains("\"kind\":\"method\"", json);
        Assert.DoesNotContain("signature", json);
    }

    [Fact]
    public void AutoLayout_PlacesBaseTypesAboveDerivedTypesAndKeepsPositions()
    {
        var model = Model();
        var arranged = AutoLayout.Arrange(Unplaced(model), model, all: false);
        var car = Assert.IsType<TypePlacement>(arranged.Find("Shop.Car"));
        var vehicle = Assert.IsType<TypePlacement>(arranged.Find("Shop.IVehicle"));

        Assert.All(arranged.Types, t => Assert.True(t.IsPlaced));
        Assert.True(vehicle.Y < car.Y);
        Assert.Equal(0, car.X % BoxMetrics.GridSize);

        var moved = arranged.With(car.Moved(500, 600));
        Assert.Equal(500, AutoLayout.Arrange(moved, model, all: false).Find("Shop.Car")?.X);
    }

    [Fact]
    public void Geometry_EdgesStartAndEndAtBoxBorders()
    {
        var model = Model();
        var document = new DiagramDocument([new TypePlacement("Shop.IVehicle", 0, 0), new TypePlacement("Shop.Car", 0, 300)]);

        var boxes = DiagramGeometry.Boxes(model, document);
        var edges = DiagramGeometry.Edges(model, boxes);

        Assert.Single(edges);
        var vehicle = boxes.First(b => b.Type.Name == "IVehicle");
        Assert.True(Math.Abs(edges[0].End.Y - vehicle.Frame.MaxY) < 0.001);
        Assert.Equal(1, boxes.First(b => b.Type.Name == "Car").MemberIndex(new CGPoint(10, 300 + BoxMetrics.HeaderHeight + 20)));
    }

    [Fact]
    public void Document_RoundTripsThroughJsonIncludingUnplacedTypes()
    {
        var folder = Repository.TemporaryFolder("vslike-diagram");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Shop" + DiagramDocument.FileExtension);
            var document = new DiagramDocument([new TypePlacement("A", 20, 40, collapsed: true), new TypePlacement("B")]) { Zoom = 1.5 };

            document.Save(path);
            var loaded = DiagramDocument.Load(path);

            Assert.Equal("Shop", DiagramDocument.Name(path));
            Assert.Equal(1.5, loaded.Zoom);
            Assert.Equal(document.Find("A"), loaded.Find("A"));
            Assert.False(loaded.Find("B")?.IsPlaced);
            Assert.True(loaded.Renamed("A", "C").Find("C")?.Collapsed);
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    [Fact]
    public void Svg_ContainsBoxesEdgesAndEscapedText()
    {
        var model = Model();
        var document = AutoLayout.Arrange(Unplaced(model), model, all: true);

        var svg = SVGExporter.Export(model, document);

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg, StringComparison.Ordinal);
        Assert.Contains("Car&lt;T&gt;", svg);
        Assert.Contains("stroke-dasharray=\"6 4\"", svg);
        Assert.Contains("- Honk(times: int): void", svg);
        Assert.Contains("x [*]", svg);
    }
}
