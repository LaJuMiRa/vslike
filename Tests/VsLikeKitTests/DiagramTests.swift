import Foundation
import Testing
@testable import VsLikeKit

@Suite("Class diagram model, layout and export")
struct DiagramTests {
    static let json = """
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
    """

    func model() throws -> DiagramModel {
        try JSONDecoder().decode(DiagramModel.self, from: Data(Self.json.utf8))
    }

    @Test func decodesEngineJSONWithOptionalFieldsAndFlags() throws {
        let model = try model()
        let car = try #require(model.find("Shop.Car"))

        #expect(car.modifiers == [.abstract, .sealed])
        #expect(car.displayName == "Car<T>")
        #expect(car.stereotype == "«abstract»")
        #expect(car.members[1].umlLine == "- Honk(times: int): void")
        #expect(car.members[1].signature == "Honk(int)")
        #expect(car.members[0].accessors == .getInit)
        #expect(model.find("System.IDisposable")?.stereotype == "«interface»")
        #expect(model.relations[1].isMany)
    }

    @Test func memberEncodesWithoutComputedFields() throws {
        let member = DiagramMember(kind: .method, name: "Run", type: "void", parameters: [DiagramParameter(name: "x", type: "int")])
        let json = String(decoding: try JSONEncoder().encode(member), as: UTF8.self)

        #expect(json.contains("\"kind\":\"method\""))
        #expect(!json.contains("signature"))
    }

    @Test func autoLayout_placesBaseTypesAboveDerivedTypesAndKeepsPositions() throws {
        let model = try model()
        let document = DiagramDocument(types: model.types.map { TypePlacement(fullName: $0.fullName, x: .nan, y: .nan) })

        let arranged = AutoLayout.arrange(document, model: model, all: false)
        let car = try #require(arranged.find("Shop.Car"))
        let vehicle = try #require(arranged.find("Shop.IVehicle"))

        #expect(arranged.types.allSatisfy { $0.isPlaced })
        #expect(vehicle.y < car.y)
        #expect(car.x.truncatingRemainder(dividingBy: BoxMetrics.gridSize) == 0)

        let moved = arranged.with(car.moved(x: 500, y: 600))
        #expect(AutoLayout.arrange(moved, model: model, all: false).find("Shop.Car")?.x == 500)
    }

    @Test func geometry_edgesStartAndEndAtBoxBorders() throws {
        let model = try model()
        let document = DiagramDocument(types: [
            TypePlacement(fullName: "Shop.IVehicle", x: 0, y: 0),
            TypePlacement(fullName: "Shop.Car", x: 0, y: 300),
        ])

        let boxes = DiagramGeometry.boxes(model: model, document: document)
        let edges = DiagramGeometry.edges(model: model, boxes: boxes)

        #expect(edges.count == 1)
        let vehicle = boxes.first { $0.type.name == "IVehicle" }!
        #expect(abs(edges[0].end.y - vehicle.frame.maxY) < 0.001)
        #expect(boxes.first { $0.type.name == "Car" }!.memberIndex(at: CGPoint(x: 10, y: 300 + BoxMetrics.headerHeight + 20)) == 1)
    }

    @Test func document_roundTripsThroughJSONIncludingUnplacedTypes() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = folder.appendingPathComponent("Shop" + DiagramDocument.fileExtension)
        var document = DiagramDocument(types: [TypePlacement(fullName: "A", x: 20, y: 40, collapsed: true), TypePlacement(fullName: "B", x: .nan, y: .nan)])
        document.zoom = 1.5

        try document.save(to: url)
        let loaded = DiagramDocument.load(url)

        #expect(DiagramDocument.name(of: url) == "Shop")
        #expect(loaded.zoom == 1.5)
        #expect(loaded.find("A") == document.find("A"))
        #expect(loaded.find("B")?.isPlaced == false)
        #expect(loaded.renamed("A", to: "C").find("C")?.collapsed == true)
    }

    @Test func svg_containsBoxesEdgesAndEscapedText() throws {
        let model = try model()
        let document = AutoLayout.arrange(DiagramDocument(types: model.types.map { TypePlacement(fullName: $0.fullName, x: .nan, y: .nan) }), model: model, all: true)

        let svg = SVGExporter.export(model: model, document: document)

        #expect(svg.hasPrefix("<svg xmlns=\"http://www.w3.org/2000/svg\""))
        #expect(svg.contains("Car&lt;T&gt;"))
        #expect(svg.contains("stroke-dasharray=\"6 4\""))
        #expect(svg.contains("- Honk(times: int): void"))
        #expect(svg.contains("x [*]"))
    }
}
