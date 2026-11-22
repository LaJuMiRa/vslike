using System.Diagnostics;
using System.Text.Json.Nodes;
using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Core.Files;
using VsLike.Diagram.Model;
using VsLike.Engine.Protocol;

namespace VsLike.Engine.Tests;

/// <summary>End-to-end tests of the engine process over JSON-RPC, against the ThreeProjects sample (App → Lib1 → Lib2).</summary>
public sealed class EngineProtocolTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(RealPath.Resolve(Path.GetTempPath()), "vslike-engine", Guid.NewGuid().ToString("N"));
    private EngineProcess _engine = null!;

    private string Solution => Path.Combine(_root, "Sample.slnx");

    private string CarFile => Path.Combine(_root, "Lib1", "Car.cs");

    public async Task InitializeAsync()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestAssets", "ThreeProjects"), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "TestAssets", "ThreeProjects"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        using (var restore = Process.Start(new DotnetCli().CreateStartInfo(["restore", Solution], workingDirectory: null))!)
        {
            var errors = restore.StandardError.ReadToEndAsync();
            await restore.StandardOutput.ReadToEndAsync();
            await errors;
            await restore.WaitForExitAsync();
            Assert.Equal(0, restore.ExitCode);
        }

        _engine = EngineProcess.Start();
        await _engine.RequestAsync("initialize", new InitializeParams("en"));
        await _engine.RequestAsync("workspace/load", new PathParams(Solution), TimeSpan.FromMinutes(3));
    }

    public async Task DisposeAsync()
    {
        await _engine.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Projects_ListsTheThreeProjects()
    {
        var projects = await _engine.RequestAsync<List<WorkspaceProject>>("workspace/projects");

        Assert.Equal(["App", "Lib1", "Lib2"], projects.Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Completion_AfterEditedText_OffersMembersAndResolvesToAnEdit()
    {
        var text = File.ReadAllText(CarFile).Replace("=> $\"{Name}: {_engine.Start()}\";", "=> _engine.;", StringComparison.Ordinal);
        await _engine.NotifyAsync("document/update", new TextParams(CarFile, text));
        var offset = text.IndexOf("_engine.;", StringComparison.Ordinal) + "_engine.".Length;

        var list = await _engine.RequestAsync<CompletionResult>("completion/list", new OffsetParams(CarFile, offset));
        var start = list.Items.Single(i => i.DisplayText == "Start");
        var edit = await _engine.RequestAsync<WorkspaceEdit>("completion/resolve", new HandleParams(CarFile, start.Handle));

        Assert.Equal(CompletionKind.Method, start.Kind);
        Assert.Contains(edit.Files.Single().Edits, e => e.NewText.Contains("Start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocumentUpdate_WithError_PublishesCompilerDiagnostic()
    {
        var text = File.ReadAllText(CarFile).Replace("\"Car\";", "42;", StringComparison.Ordinal);

        await _engine.NotifyAsync("document/update", new TextParams(CarFile, text));
        var notification = await _engine.WaitForNotificationAsync(
            "workspace/diagnostics",
            n => n["params"]?["diagnostics"]?.AsArray().Any(d => d?["id"]?.GetValue<string>() == "CS0029") == true,
            TimeSpan.FromSeconds(60));

        Assert.Equal(CarFile, notification["params"]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task StyleCop_WhenEnabled_ReportsRulesAndOffersFixAllInDocument()
    {
        Assert.True(await _engine.RequestAsync<bool>("styleCop/set", new StyleCopParams(true), TimeSpan.FromMinutes(3)));
        var text = File.ReadAllText(CarFile).Replace("{\n", "{   \n", StringComparison.Ordinal).Replace("= new();\n", "= new();  \n", StringComparison.Ordinal);
        await _engine.NotifyAsync("document/update", new TextParams(CarFile, text));

        var diagnostics = await _engine.RequestAsync<List<DiagnosticInfo>>("document/diagnostics", new PathParams(CarFile));
        var actions = await _engine.RequestAsync<List<CodeAction>>("codeActions/list", new RangeParams(CarFile, text.IndexOf("{   ", StringComparison.Ordinal) + 1, 0));
        var fixAll = actions.Single(a => a.Group == "SA1028" && a.Title.StartsWith("Fix all in document", StringComparison.Ordinal));
        var edit = await _engine.RequestAsync<WorkspaceEdit>("codeActions/resolve", new CodeActionParams(fixAll.Handle));

        Assert.Contains(diagnostics, d => d.Category == "StyleCop" && d.Id == "SA1028");
        Assert.Equal(2, edit.Files.Single().Edits.Count(e => e.NewText.Length == 0));
    }

    [Fact]
    public async Task CodeActions_ForUndeclaredMethod_OfferGenerateMethod()
    {
        var text = File.ReadAllText(CarFile).Replace("=> $\"{Name}: {_engine.Start()}\";", "=> Honk();", StringComparison.Ordinal);
        await _engine.NotifyAsync("document/update", new TextParams(CarFile, text));
        var offset = text.IndexOf("Honk", StringComparison.Ordinal);

        var actions = await _engine.RequestAsync<List<CodeAction>>("codeActions/list", new RangeParams(CarFile, offset, 4));
        var generate = actions.First(a => a.Title.Contains("Honk", StringComparison.Ordinal) && a.Title.StartsWith("Generate method", StringComparison.Ordinal));
        var edit = await _engine.RequestAsync<WorkspaceEdit>("codeActions/resolve", new CodeActionParams(generate.Handle));

        Assert.Contains(edit.Files.Single(f => f.FilePath == CarFile).Edits, e => e.NewText.Contains("Honk()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diagram_ReadsModelAndWritesMembersAndNewTypes()
    {
        var model = await _engine.RequestAsync<DiagramModel>("diagram/model");
        var car = model.Types.Single(t => t.FullName == "Lib1.Car");
        Assert.Contains(model.Relations, r => r.From == "Lib1.Car" && r.To == "Lib2.Engine" && r.Kind == RelationKind.Composition);

        var member = await _engine.RequestAsync<DiagramMember>("diagram/parseMember", new ParseMemberParams(MemberKind.Method, "+ Honk(times: int): void", "Car"));
        var edited = await _engine.RequestAsync<SourceResult>("diagram/addMember", new AddMemberParams(File.ReadAllText(CarFile), car.FullName, member));
        var created = await _engine.RequestAsync<NewTypeResult>("diagram/newType", new NewTypeParams(DiagramTypeKind.Interface, "IVehicle", Path.Combine(_root, "Lib1", "Models")));

        Assert.Contains("public void Honk(int times)", edited.Text, StringComparison.Ordinal);
        Assert.Equal("Lib1.Models.IVehicle", created.FullName);
        Assert.StartsWith("namespace Lib1.Models\n{\n    public interface IVehicle", created.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var response = await _engine.RawRequestAsync("does/not/exist");

        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
    }
}
