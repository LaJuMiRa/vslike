using System.Diagnostics;
using VsLike.Kit;
using Xunit;

namespace VsLike.Kit.Tests;

/// <summary>Engine, dotnet CLI and debugger end to end (against the real .NET SDK, engine and netcoredbg).</summary>
[Collection("Integration")]
public sealed class IntegrationTests
{
    private readonly CommandRunner _runner = CommandRunner.Dotnet("en");

    [Fact(Timeout = 300_000)]
    public async Task Engine_LoadsSolutionAndServesIntelliSenseDiagnosticsAndDiagram()
    {
        var folder = Repository.CopySample("ThreeProjects");
        try
        {
            var solution = Path.Combine(folder, "Sample.slnx");
            await _runner.RunChecked(["restore", solution]);
            using var engine = new EngineClient(Repository.EngineDll, _runner);
            engine.Start();

            var info = await engine.Initialize("en");
            var loaded = await engine.Load(solution);
            Assert.NotEmpty(info.SdkVersion);
            Assert.Equal(new HashSet<string> { "App", "Lib1", "Lib2" }, loaded.Projects.Select(p => p.Name).ToHashSet());

            var car = Path.Combine(folder, "Lib1/Car.cs");
            var original = await File.ReadAllTextAsync(car);
            var edited = original.Replace("=> $\"{Name}: {_engine.Start()}\";", "=> _engine.;", StringComparison.Ordinal);
            engine.UpdateDocument(car, edited);
            var offset = edited.IndexOf("_engine.;", StringComparison.Ordinal) + "_engine.".Length;
            var completion = Assert.IsType<CompletionList>(await engine.Completion(car, offset));
            Assert.Contains(completion.Items, i => i.DisplayText == "Start" && i.Kind == CompletionKind.Method);

            engine.UpdateDocument(car, original.Replace("\"Car\";", "42;", StringComparison.Ordinal));
            var diagnostics = await engine.Diagnostics(car);
            Assert.Contains(diagnostics, d => d.Code == "CS0029" && d.Severity == DiagnosticSeverity.Error);

            var model = await engine.DiagramModel();
            Assert.Contains(model.Find("Lib1.Car")!.Members, m => m.Name == "Drive");
            Assert.Contains(model.Relations, r => r.From == "Lib1.Car" && r.To == "Lib2.Engine" && r.Kind == RelationKind.Composition);
            var member = Assert.IsType<DiagramMember>(await engine.ParseMember(MemberKind.Property, "+ Speed: int", "Car"));
            var source = await engine.AddMember(original, "Lib1.Car", member);
            Assert.Contains("public int Speed { get; set; }", source);

            await engine.Stop();
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    [Fact(Timeout = 300_000)]
    public async Task BuildAndTests_ReportResultsOfTheCalcSample()
    {
        var folder = Repository.CopySample("WithTests");
        try
        {
            var solution = Path.Combine(folder, "Calc.slnx");
            var project = Path.Combine(folder, "Calc.Tests/Calc.Tests.csproj");

            var build = await new BuildService(_runner).Build(solution, "Debug", BuildKind.Build);
            var tests = new TestService(_runner);
            var discovered = await tests.Discover(project, "Debug");
            var run = await tests.Run(project, "Debug", null);

            Assert.True(build.Succeeded, build.Process.AllText);
            Assert.Contains("Calc.Tests.CalculatorTests.Add_Works", discovered.Tests);
            Assert.Equal(4, run.Results.Count);
            Assert.Equal(TestOutcome.Failed, run.Results.First(r => r.FullName.EndsWith("Add_Wrong", StringComparison.Ordinal)).Outcome);
            Assert.True(ProjectFile.Load(project).IsTestProject);
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Debugger_StopsAtBreakpointAndShowsLocals()
    {
        var adapter = Repository.Adapter();
        Assert.SkipWhen(adapter is null, "netcoredbg missing: run linux/build-linux.sh once (it caches netcoredbg)");
        var folder = Repository.CopySample("ThreeProjects");
        try
        {
            var project = Path.Combine(folder, "App/App.csproj");
            Assert.True((await new BuildService(_runner).Build(project, "Debug", BuildKind.Build)).Succeeded);
            var program = Assert.IsType<string>(await new BuildService(_runner).TargetPath(project, "Debug"));
            var file = Path.Combine(folder, "App/Program.cs");

            var (session, states) = await DebugSession.Start(
                adapter!,
                new LaunchConfiguration(program, [], Path.Combine(folder, "App"), new Dictionary<string, string>()),
                new Dictionary<string, IReadOnlyList<SourceBreakpoint>> { [file] = [new SourceBreakpoint(4)] },
                _runner.Executable);
            Assert.Single(states[file]);
            var thread = await FirstStop(session);
            var frame = (await session.StackTrace(thread)).First();
            var scope = (await session.Scopes(frame.Id)).First();
            var locals = await session.Variables(scope.VariablesReference);
            var name = await session.Evaluate("car.Name", frame.Id, "watch");

            Assert.Equal(4, frame.Line);
            Assert.Contains(locals, l => l.Name == "car");
            Assert.Equal("\"Demo\"", name.Value);
            await session.Stop();
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    /// <summary>New console projects get a Program class with Main; templates without that option (class library) are created as they are.</summary>
    [Fact(Timeout = 300_000)]
    public async Task CreateProject_WritesProgramWithNamespaceAndMain()
    {
        var folder = Repository.TemporaryFolder("vslike-new");
        try
        {
            var service = new ProjectService(_runner);

            var app = await service.CreateProject("console", "Garage", folder);
            var program = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(app)!, "Program.cs"));
            Assert.Contains("namespace Garage\n{\n    class Program\n    {\n        static void Main(string[] args)", program);
            Assert.Contains("Console.WriteLine(\"Hello, World!\");", program);

            var library = await service.CreateProject("classlib", "Garage.Core", folder);
            var class1 = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(library)!, "Class1.cs"));
            Assert.Contains("namespace Garage.Core\n{\n    public class Class1", class1);
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    /// <summary>A program started outside the debugger waits in the startup hook until the debugger has attached.</summary>
    [Fact(Timeout = 300_000)]
    public async Task Debugger_AttachesToProgramHeldByStartupHook()
    {
        var adapter = Repository.Adapter();
        Assert.SkipWhen(adapter is null, "netcoredbg missing: run linux/build-linux.sh once (it caches netcoredbg)");
        var hook = DebugStartupHook.Locate();
        Assert.SkipWhen(hook is null, "build the engine solution first");
        var folder = Repository.CopySample("ThreeProjects");
        try
        {
            var project = Path.Combine(folder, "App/App.csproj");
            Assert.True((await new BuildService(_runner).Build(project, "Debug", BuildKind.Build)).Succeeded);
            var program = Assert.IsType<string>(await new BuildService(_runner).TargetPath(project, "Debug"));
            var file = Path.Combine(folder, "App/Program.cs");
            var readyFile = Path.Combine(folder, "debug-ready");

            var info = new ProcessStartInfo(_runner.Executable) { WorkingDirectory = Path.Combine(folder, "App"), RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add(program);
            info.Environment.Clear();
            foreach (var (key, value) in DebugStartupHook.Environment(_runner.ChildEnvironment(), hook!, readyFile))
            {
                info.Environment[key] = value;
            }

            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            try
            {
                var (session, _) = await DebugSession.Attach(
                    adapter!,
                    process.Id,
                    new Dictionary<string, IReadOnlyList<SourceBreakpoint>> { [file] = [new SourceBreakpoint(3)] },
                    _runner.Executable,
                    readyFile);
                var thread = await FirstStop(session);
                Assert.Equal(3, (await session.StackTrace(thread)).First().Line);
                await session.Resume(thread);
                using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await process.WaitForExitAsync(watchdog.Token);

                Assert.Equal(0, process.ExitCode);
                Assert.NotEmpty(await output);
                await session.Stop();
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
        }
        finally
        {
            Repository.Delete(folder);
        }
    }

    private static async Task<int> FirstStop(DebugSession session)
    {
        await foreach (var debugEvent in session.Events.ReadAllAsync())
        {
            switch (debugEvent)
            {
                case DebugEvent.Stopped stopped:
                    return stopped.ThreadId;
                case DebugEvent.Terminated:
                    throw new InvalidOperationException("The program ended before it stopped.");
            }
        }

        throw new InvalidOperationException("The debugger ended before the program stopped.");
    }
}
