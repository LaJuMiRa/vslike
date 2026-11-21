import AppKit
import Foundation
import Testing
@testable import VsLike
@testable import VsLikeKit

/// Copies a sample from engine/tests/TestAssets into a temporary folder and restores it.
@MainActor
enum Samples {
    static let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

    static func copy(_ name: String) async throws -> URL {
        let target = RealPath.resolve(FileManager.default.temporaryDirectory).appendingPathComponent("vslike-app-\(UUID().uuidString)")
        try FileManager.default.copyItem(at: repository.appendingPathComponent("engine/tests/TestAssets/\(name)"), to: target)
        let solution = try FileManager.default.contentsOfDirectory(atPath: target.path).first { $0.hasSuffix(".slnx") }!
        try await CommandRunner.dotnet(language: "en").runChecked(["restore", target.appendingPathComponent(solution).path])
        return target
    }

    /// Polls until the condition holds or the timeout passes.
    static func wait(_ seconds: Double = 60, _ condition: @MainActor () -> Bool) async -> Bool {
        let deadline = Date().addingTimeInterval(seconds)
        while Date() < deadline {
            if condition() { return true }
            try? await Task.sleep(for: .milliseconds(100))
        }
        return condition()
    }
}

@MainActor
@Suite("App workspace against the real engine", .serialized)
struct WorkspaceTests {
    let preferences = Preferences(defaults: UserDefaults(suiteName: "vslike-tests-\(UUID().uuidString)")!)

    private func openSample(_ name: String = "ThreeProjects") async throws -> (Workspace, URL) {
        let folder = try await Samples.copy(name)
        let workspace = Workspace(preferences: preferences)
        workspace.confirmSaveHandler = { _ in .discard }
        await workspace.open(folder)
        #expect(workspace.engineStatus == .ready, "engine: \(workspace.engineStatus) \(workspace.output.lines(in: .engine).map(\.text))")
        return (workspace, folder)
    }

    @Test(.timeLimit(.minutes(3)))
    func open_folderPicksTheSolutionBuildsTreeAndSelectsStartupProject() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }

        #expect(workspace.solution?.projects.count == 3)
        #expect(workspace.tree.map(\.name).prefix(3) == ["App", "Lib1", "Lib2"])
        #expect(workspace.build.startupProjectName == "App")
        #expect(workspace.projects.count == 3)
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(3)))
    func editing_publishesLiveDiagnosticsAndQuickActionFixesThem() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        let document = try #require(workspace.openFile(folder.appendingPathComponent("Lib1/Car.cs")))

        let range = (document.text as NSString).range(of: "=> $\"{Name}: {_engine.Start()}\";")
        document.replace(range, with: "=> Honk();")
        let reported = await Samples.wait { workspace.diagnostics.diagnostics(for: document.path).contains { $0.code == "CS0103" } }
        #expect(reported)
        #expect(document.isDirty)

        let offset = (document.text as NSString).range(of: "Honk").location
        let actions = await workspace.codeActions(document, range: NSRange(location: offset, length: 4))
        // "Generate method 'Honk'" (the title follows the system language).
        let generate = try #require(actions.first { $0.title.contains("Honk") && !$0.title.lowercased().contains("lokal") && !$0.title.lowercased().contains("local") })
        await workspace.perform(generate)

        #expect(document.text.contains("private string Honk()"))
        let cleared = await Samples.wait { !workspace.diagnostics.diagnostics(for: document.path).contains { $0.code == "CS0103" } }
        #expect(cleared)
        document.undoManager.undo()
        #expect(!document.text.contains("private string Honk()"))
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(4)))
    func styleCop_toggleIsPersistedAndFixAllCleansTheFile() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        let document = try #require(workspace.openFile(folder.appendingPathComponent("Lib2/Engine.cs")))
        document.replace(NSRange(location: 0, length: document.storage.length), with: document.text.replacingOccurrences(of: "{\n", with: "{   \n"))

        await workspace.toggleStyleCop()
        #expect(workspace.styleCopEnabled)
        #expect(SolutionSettings.load(for: workspace.solutionURL!).styleCopEnabled == true)
        let found = await Samples.wait { workspace.diagnostics.diagnostics(for: document.path).contains { $0.code == "SA1028" } }
        #expect(found)

        await workspace.fixAllStyleCop(in: document)

        #expect(!document.text.contains("{   \n"))
        // SA1600: the undocumented property gets a comment in StyleCop's wording.
        #expect(document.text.contains("/// Gets or initializes the horsepower."))
        #expect(!workspace.diagnostics.diagnostics(for: document.path).contains { $0.code == "SA1600" })
        #expect(workspace.diagnostics.diagnostics(for: document.path).filter(\.isStyleCop).count < 3)
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(3)))
    func diagram_createsTypeAddsMemberAndUndoesBoth() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        await workspace.openSolutionDiagram()
        guard case let .diagram(diagram) = workspace.activeTab else {
            Issue.record("no diagram tab")
            return
        }
        let loaded = await Samples.wait { diagram.boxes.count == 2 }
        #expect(loaded)
        #expect(FileManager.default.fileExists(atPath: folder.appendingPathComponent("Sample.cdiagram.json").path))

        let lib1 = folder.appendingPathComponent("Lib1")
        let model = DiagramEditorModel(url: lib1.appendingPathComponent("Lib1.cdiagram.json"), workspace: workspace)
        await model.refresh()
        await model.createType(.class, name: "Truck", at: CGPoint(x: 400, y: 40))
        let truckFile = lib1.appendingPathComponent("Truck.cs")
        #expect(try String(contentsOf: truckFile, encoding: .utf8).contains("namespace Lib1;"))
        let truck = try #require(model.model.find("Lib1.Truck"))
        await model.addMember(to: truck, kind: .property, text: "+ Wheels: int")
        #expect(try String(contentsOf: truckFile, encoding: .utf8).contains("public int Wheels { get; set; }"))
        let car = try #require(model.model.find("Lib1.Car"))
        await model.connect(from: "Lib1.Truck", to: car.fullName)
        #expect(try String(contentsOf: truckFile, encoding: .utf8).contains("class Truck : Car"))

        await model.undo()
        await model.undo()
        #expect(try !String(contentsOf: truckFile, encoding: .utf8).contains("Wheels"))
        await model.undo()
        #expect(!FileManager.default.fileExists(atPath: truckFile.path))
        await model.redo()
        #expect(FileManager.default.fileExists(atPath: truckFile.path))
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(3)))
    func diagram_opensCdFileWithItsPositionsAndFindsTheEnclosingSolution() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = folder.appendingPathComponent("Lib1/Model.cd")
        try """
        <?xml version="1.0" encoding="utf-8"?>
        <ClassDiagram MajorVersion="1" MinorVersion="1">
          <Class Name="Lib1.Car">
            <Position X="2" Y="1" Width="1.5" />
            <TypeIdentifier><HashCode>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=</HashCode><FileName>Car.cs</FileName></TypeIdentifier>
          </Class>
        </ClassDiagram>
        """.write(to: url, atomically: true, encoding: .utf8)

        #expect(AppDelegate.enclosingSolution(of: url)?.lastPathComponent == "Sample.slnx")
        #expect(workspace.openFile(url) == nil)
        guard case let .diagram(diagram) = workspace.activeTab else {
            Issue.record("no diagram tab")
            return
        }
        let loaded = await Samples.wait { diagram.boxes.contains { $0.type.fullName == "Lib1.Car" } }
        #expect(loaded)
        let car = try #require(diagram.boxes.first { $0.type.fullName == "Lib1.Car" })
        #expect(car.frame.minX == 192)
        #expect(car.frame.minY == 96)
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(4)))
    func run_andDebug_startTheProgramInATerminalTabAndStopAtTheFirstLine() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }

        workspace.build.startWithoutDebugging()
        let ran = await Samples.wait(40) { workspace.terminals.sessions.contains { $0.isRun && !$0.isRunning } }
        #expect(ran, "sessions: \(workspace.terminals.sessions.map { "\($0.title) running=\($0.isRunning) pid=\($0.processID)" }) status: \(workspace.status.text)")
        #expect(workspace.terminals.sessions.first { $0.isRun }?.exitCode == 0)
        #expect(workspace.bottomTab == .terminal)

        let program = try #require(workspace.openFile(folder.appendingPathComponent("App/Program.cs")))
        program.toggleBreakpoint(line: 3)
        workspace.debugger.breakpointsChanged(in: program)
        await workspace.debugger.start()
        let stopped = await Samples.wait(120) { workspace.debugger.isStopped }
        #expect(stopped)
        #expect(workspace.debugger.frames.first?.line == 3)
        #expect(workspace.bottomTab == .debug)
        let debugTab = try #require(workspace.terminals.sessions.last)
        #expect(debugTab.isRun && debugTab.isRunning)

        await workspace.debugger.resume()
        #expect(workspace.bottomTab == .terminal)
        let finished = await Samples.wait(30) { !debugTab.isRunning }
        #expect(finished, "active=\(workspace.debugger.isActive) stopped=\(workspace.debugger.isStopped) reason=\(workspace.debugger.stopReason ?? "") console=\(workspace.debugger.console.map(\.text))")
        #expect(debugTab.exitCode == 0)
        await workspace.debugger.stop()
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(4)))
    func build_reportsErrorsInProblemsAndSucceedsAfterFix() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        let program = folder.appendingPathComponent("App/Program.cs")
        let original = try String(contentsOf: program, encoding: .utf8)
        try (original + "\nint broken = \"text\";\n").write(to: program, atomically: true, encoding: .utf8)

        let failed = await workspace.build.run(.build)
        #expect(failed?.succeeded == false)
        #expect(workspace.diagnostics.build.contains { $0.code == "CS0029" && $0.filePath == program.path })
        #expect(workspace.status.style == .error)

        try original.write(to: program, atomically: true, encoding: .utf8)
        let fixed = await workspace.build.run(.build)
        #expect(fixed?.succeeded == true)
        #expect(workspace.status.style == .success)
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(4)))
    func tests_discoverAndRunInTheCalcSample() async throws {
        let (workspace, folder) = try await openSample("WithTests")
        defer { try? FileManager.default.removeItem(at: folder) }
        #expect(workspace.tests.testProjects.map(\.name) == ["Calc.Tests"])

        workspace.tests.run()
        let finished = await Samples.wait(180) { !workspace.tests.isBusy && !workspace.tests.results.isEmpty }

        #expect(finished)
        #expect(workspace.tests.passedCount == 3)
        #expect(workspace.tests.failedCount == 1)
        let tree = workspace.tests.tree
        #expect(tree.first?.outcome == .failed)
        _ = await workspace.closeAll()
    }

    @Test(.timeLimit(.minutes(3)))
    func applyEdit_changesClosedFilesOnDiskAndOpenDocumentsUndoably() async throws {
        let (workspace, folder) = try await openSample()
        defer { try? FileManager.default.removeItem(at: folder) }
        let car = folder.appendingPathComponent("Lib1/Car.cs")
        let engineFile = folder.appendingPathComponent("Lib2/Engine.cs")
        let document = try #require(workspace.openFile(car))
        let openOffset = (document.text as NSString).range(of: "Car\";").location
        let closedText = try String(contentsOf: engineFile, encoding: .utf8)
        let closedOffset = (closedText as NSString).range(of: "100").location

        await workspace.apply(WorkspaceEdit(files: [
            FileEdit(filePath: car.path, edits: [TextEdit(start: openOffset, length: 3, newText: "Auto")]),
            FileEdit(filePath: engineFile.path, edits: [TextEdit(start: closedOffset, length: 3, newText: "150")]),
        ]), actionName: "Test")

        #expect(document.text.contains("\"Auto\";"))
        #expect(try String(contentsOf: engineFile, encoding: .utf8).contains("= 150;"))
        document.undoManager.undo()
        #expect(document.text.contains("\"Car\";"))
        _ = await workspace.closeAll()
    }
}
