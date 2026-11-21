import Foundation
import Testing
@testable import VsLikeKit

@Suite("Class diagram files (.cd)")
struct ClassDiagramFileTests {
    private let sample = """
    <?xml version="1.0" encoding="utf-8"?>
    <ClassDiagram MajorVersion="1" MinorVersion="1">
      <Comment CommentText="Domain model">
        <Position X="4.25" Y="0.5" Height="0.5" Width="1.5" />
      </Comment>
      <Class Name="Demo.Car" Collapsed="true">
        <Position X="0.5" Y="0.75" Width="1.5" />
        <TypeIdentifier>
          <HashCode>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=</HashCode>
          <FileName>Car.cs</FileName>
        </TypeIdentifier>
      </Class>
      <Interface Name="Demo.Store&lt;TKey, TValue&gt;">
        <Position X="2.5" Y="0.5" Width="1.75" />
        <TypeIdentifier>
          <HashCode>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=</HashCode>
          <FileName>Data\\Store.cs</FileName>
        </TypeIdentifier>
      </Interface>
    </ClassDiagram>
    """

    @Test func readsTypesPositionsInInchesAndCollapsedState() throws {
        let document = try ClassDiagramFile.read(Data(sample.utf8))

        #expect(document.types.map(\.fullName) == ["Demo.Car", "Demo.Store`2"])
        #expect(document.types[0].x == 48)
        #expect(document.types[0].y == 72)
        #expect(document.types[0].collapsed)
        #expect(document.types[1].x == 240)
        #expect(!document.types[1].collapsed)
    }

    @Test func convertsGenericNamesToDiagramNames() {
        #expect(ClassDiagramFile.fullName(fromFileName: "Demo.Box<T>") == "Demo.Box`1")
        #expect(ClassDiagramFile.fullName(fromFileName: "Demo.Map<TKey, List<TValue>>") == "Demo.Map`2")
        #expect(ClassDiagramFile.fullName(fromFileName: "Demo.Outer.Inner") == "Demo.Outer.Inner")
    }

    @Test func writeUpdatesPositionsAddsAndRemovesTypesAndKeepsOtherContent() throws {
        var document = try ClassDiagramFile.read(Data(sample.utf8))
        document = document.with(document.types[0].moved(x: 96, y: 144).toggled())
        document = document.without("Demo.Store`2")
        document = document.with(TypePlacement(fullName: "Demo.Engine", x: 192, y: 48))
        document = document.with(TypePlacement(fullName: "System.Object", x: 0, y: 0))
        let info = ["Demo.Engine": ClassDiagramFile.TypeInfo(tag: "Struct", name: "Demo.Engine", fileName: "Parts\\Engine.cs")]

        let data = try ClassDiagramFile.write(document, existing: Data(sample.utf8), types: info)
        let text = String(decoding: data, as: UTF8.self)
        let reread = try ClassDiagramFile.read(data)

        #expect(reread.types.map(\.fullName) == ["Demo.Car", "Demo.Engine"])
        #expect(reread.find("Demo.Car")?.x == 96)
        #expect(reread.find("Demo.Car")?.y == 144)
        #expect(reread.find("Demo.Car")?.collapsed == false)
        #expect(text.contains("CommentText=\"Domain model\""))
        #expect(text.contains("<Struct Name=\"Demo.Engine\">"))
        #expect(text.contains("<FileName>Parts\\Engine.cs</FileName>"))
        #expect(!text.contains("Store"))
        #expect(!text.contains("System.Object"))
    }

    @Test func diagramDocumentLoadsAndSavesThroughTheCdFormat() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("vslike-cd-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = folder.appendingPathComponent("Model.cd")
        try Data(sample.utf8).write(to: url)

        #expect(DiagramDocument.isDiagram(url))
        #expect(DiagramDocument.name(of: url) == "Model")
        let document = DiagramDocument.load(url)
        try document.with(document.types[0].moved(x: 480, y: 48)).save(to: url)

        #expect(DiagramDocument.load(url).find("Demo.Car")?.x == 480)
        #expect(try String(contentsOf: url, encoding: .utf8).contains("<ClassDiagram"))
    }
}
