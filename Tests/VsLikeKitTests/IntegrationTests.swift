import Foundation
import Testing
@testable import VsLikeKit

/// Paths of the repository for tests that run against the real .NET SDK, engine and debugger.
enum Repository {
    static let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

    static var engineDLL: URL {
        if let override = ProcessInfo.processInfo.environment["VSLIKE_ENGINE_DLL"] {
            return URL(fileURLWithPath: override)
        }
        return root.appendingPathComponent("engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll")
    }

    /// Copies a sample solution from engine/tests/TestAssets into a fresh temporary folder (symlinks resolved, as
    /// the debugger reports real paths).
    static func copySample(_ name: String) throws -> URL {
        let target = RealPath.resolve(FileManager.default.temporaryDirectory).appendingPathComponent("vslike-swift-\(UUID().uuidString)")
        try FileManager.default.copyItem(at: root.appendingPathComponent("engine/tests/TestAssets/\(name)"), to: target)
        return target
    }
}

@Suite("Engine, dotnet CLI and debugger end to end", .serialized)
struct IntegrationTests {
    let runner: CommandRunner

    init() throws {
        runner = try CommandRunner.dotnet(language: "en")
    }

    @Test(.timeLimit(.minutes(5)))
    func engine_loadsSolutionAndServesIntelliSenseDiagnosticsAndDiagram() async throws {
        let folder = try Repository.copySample("ThreeProjects")
        defer { try? FileManager.default.removeItem(at: folder) }
        let solution = folder.appendingPathComponent("Sample.slnx")
        try await runner.runChecked(["restore", solution.path])
        let engine = EngineClient(engineDLL: Repository.engineDLL, runner: runner)
        try engine.start()

        let info = try await engine.initialize(culture: "en")
        let loaded = try await engine.load(solution.path)
        #expect(!info.sdkVersion.isEmpty)
        #expect(Set(loaded.projects.map(\.name)) == ["App", "Lib1", "Lib2"])

        let car = folder.appendingPathComponent("Lib1/Car.cs").path
        let original = try String(contentsOfFile: car, encoding: .utf8)
        let edited = original.replacingOccurrences(of: "=> $\"{Name}: {_engine.Start()}\";", with: "=> _engine.;")
        engine.updateDocument(car, text: edited)
        let offset = (edited as NSString).range(of: "_engine.;").location + "_engine.".utf16.count
        let completion = try #require(try await engine.completion(car, offset: offset))
        #expect(completion.items.contains { $0.displayText == "Start" && $0.kind == .method })

        engine.updateDocument(car, text: original.replacingOccurrences(of: "\"Car\";", with: "42;"))
        let diagnostics = try await engine.diagnostics(car)
        #expect(diagnostics.contains { $0.code == "CS0029" && $0.severity == .error })

        let model = try await engine.diagramModel()
        #expect(model.find("Lib1.Car")?.members.contains { $0.name == "Drive" } == true)
        #expect(model.relations.contains { $0.from == "Lib1.Car" && $0.to == "Lib2.Engine" && $0.kind == .composition })
        let member = try #require(try await engine.parseMember(kind: .property, text: "+ Speed: int", owner: "Car"))
        let source = try await engine.addMember(source: original, type: "Lib1.Car", member: member)
        #expect(source?.contains("public int Speed { get; set; }") == true)

        await engine.stop()
    }

    @Test(.timeLimit(.minutes(5)))
    func buildAndTests_reportResultsOfTheCalcSample() async throws {
        let folder = try Repository.copySample("WithTests")
        defer { try? FileManager.default.removeItem(at: folder) }
        let solution = folder.appendingPathComponent("Calc.slnx").path
        let project = folder.appendingPathComponent("Calc.Tests/Calc.Tests.csproj").path

        let build = try await BuildService(runner: runner).build(target: solution, configuration: "Debug", kind: .build)
        let tests = TestService(runner: runner)
        let discovered = try await tests.discover(project: project, configuration: "Debug")
        let run = try await tests.run(project: project, configuration: "Debug", filter: nil)

        #expect(build.succeeded, "\(build.process.allText)")
        #expect(discovered.tests.contains("Calc.Tests.CalculatorTests.Add_Works"))
        #expect(run.results.count == 4)
        #expect(run.results.first { $0.fullName.hasSuffix("Add_Wrong") }?.outcome == .failed)
        #expect(try ProjectFile.load(project).isTestProject)
    }

    @Test(.timeLimit(.minutes(2)))
    func debugger_stopsAtBreakpointAndShowsLocals() async throws {
        let adapter = try #require(DebugSession.locateAdapter(), "netcoredbg missing: run ./build.sh once (it caches netcoredbg)")
        let folder = try Repository.copySample("ThreeProjects")
        defer { try? FileManager.default.removeItem(at: folder) }
        let project = folder.appendingPathComponent("App/App.csproj").path
        let build = try await BuildService(runner: runner).build(target: project, configuration: "Debug", kind: .build)
        #expect(build.succeeded)
        let program = try #require(try await BuildService(runner: runner).targetPath(project: project, configuration: "Debug"))
        let file = folder.appendingPathComponent("App/Program.cs").path

        let (session, states) = try await DebugSession.start(
            adapter: adapter,
            launch: LaunchConfiguration(program: program, workingDirectory: folder.appendingPathComponent("App").path),
            breakpoints: [file: [SourceBreakpoint(line: 4)]],
            dotnet: runner.executable
        )
        #expect(states[file]?.count == 1)
        var stoppedThread: Int?
        for await event in session.events {
            if case let .stopped(_, threadID, _) = event {
                stoppedThread = threadID
                break
            }
            if case .terminated = event {
                break
            }
        }
        let thread = try #require(stoppedThread)
        let frame = try #require(try await session.stackTrace(threadID: thread).first)
        let scope = try #require(try await session.scopes(frameID: frame.id).first)
        let locals = try await session.variables(reference: scope.variablesReference)
        let name = try await session.evaluate("car.Name", frameID: frame.id, context: "watch")

        #expect(frame.line == 4)
        #expect(locals.contains { $0.name == "car" })
        #expect(name.value == "\"Demo\"")
        await session.stop()
    }

    /// New console projects get a Program class with Main in the project's namespace; templates without that option
    /// (class library) are created as they are.
    @Test
    func createProject_writesProgramWithNamespaceAndMain() async throws {
        let folder = RealPath.resolve(FileManager.default.temporaryDirectory).appendingPathComponent("vslike-new-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let service = ProjectService(runner: runner)

        let app = try await service.createProject(template: "console", name: "Garage", in: folder)
        let program = try String(contentsOf: app.deletingLastPathComponent().appendingPathComponent("Program.cs"), encoding: .utf8)
        #expect(program.contains("namespace Garage\n{\n    class Program\n    {\n        static void Main(string[] args)"))
        #expect(program.contains("static void Main(string[] args)"))
        #expect(program.contains("Console.WriteLine(\"Hello, World!\");"))

        let library = try await service.createProject(template: "classlib", name: "Garage.Core", in: folder)
        let class1 = try String(contentsOf: library.deletingLastPathComponent().appendingPathComponent("Class1.cs"), encoding: .utf8)
        #expect(class1.contains("namespace Garage.Core\n{\n    public class Class1"))
    }

    /// A program started outside the debugger (as in the terminal) waits in the startup hook until the debugger has
    /// attached, so a breakpoint on the first statement is hit; its output stays on its own stdout.
    @Test
    func debugger_attachesToProgramHeldByStartupHook() async throws {
        let adapter = try #require(DebugSession.locateAdapter(), "netcoredbg missing: run ./build.sh once (it caches netcoredbg)")
        let hook = try #require(DebugStartupHook.locate(), "build the engine solution first")
        let folder = try Repository.copySample("ThreeProjects")
        defer { try? FileManager.default.removeItem(at: folder) }
        let project = folder.appendingPathComponent("App/App.csproj").path
        #expect(try await BuildService(runner: runner).build(target: project, configuration: "Debug", kind: .build).succeeded)
        let program = try #require(try await BuildService(runner: runner).targetPath(project: project, configuration: "Debug"))
        let file = folder.appendingPathComponent("App/Program.cs").path
        let readyFile = folder.appendingPathComponent("debug-ready")

        let process = Process()
        process.executableURL = runner.executable
        process.arguments = [program]
        process.currentDirectoryURL = folder.appendingPathComponent("App")
        process.environment = DebugStartupHook.environment(runner.childEnvironment(), hook: hook, readyFile: readyFile)
        let output = Pipe()
        process.standardOutput = output
        // waitUntilExit can hang on the main thread of an async test; the handler is registered before run(), and the
        // stream buffers the status even if the program exits before it is awaited.
        let exit = AsyncStream<Int32>.makeStream()
        process.terminationHandler = { finished in
            exit.continuation.yield(finished.terminationStatus)
            exit.continuation.finish()
        }
        try process.run()
        defer { if process.isRunning { process.terminate() } }

        let (session, _) = try await DebugSession.attach(
            adapter: adapter,
            processID: process.processIdentifier,
            breakpoints: [file: [SourceBreakpoint(line: 3)]],
            dotnet: runner.executable,
            readyFile: readyFile
        )
        var stoppedThread: Int?
        for await event in session.events {
            if case let .stopped(_, threadID, _) = event {
                stoppedThread = threadID
                break
            }
            if case .terminated = event {
                break
            }
        }
        let thread = try #require(stoppedThread)
        #expect(try await session.stackTrace(threadID: thread).first?.line == 3)
        try await session.resume(threadID: thread)
        let watchdog = Task {
            try await Task.sleep(for: .seconds(60))
            process.terminate()
        }
        defer { watchdog.cancel() }
        let text = String(decoding: output.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        var status: Int32?
        for await code in exit.stream {
            status = code
        }
        #expect(status == 0)
        #expect(!text.isEmpty)
        await session.stop()
    }
}
