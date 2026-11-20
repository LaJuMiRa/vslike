using System.Diagnostics;
using VsLike.Core.Code;

namespace VsLike.Roslyn.Tests;

[Collection(SampleSolutionCollection.Name)]
public sealed class RoslynWorkspaceTests(SampleSolutionFixture fixture)
{
    private RoslynWorkspace Workspace => fixture.Workspace;

    [Fact]
    public void Load_ReadsThreeProjectsWithReferences()
    {
        Assert.Equal(["App", "Lib1", "Lib2"], Workspace.Projects.Select(p => p.Name).Order());
        var app = Workspace.Projects.Single(p => p.Name == "App");
        Assert.Contains(app.ProjectReferences, r => r.EndsWith("Lib1.csproj", StringComparison.Ordinal));
        Assert.Equal("Sample.App", app.RootNamespace);
        Assert.True(Workspace.ContainsDocument(fixture.PathOf("Lib1/Car.cs")));
    }

    [Fact]
    public async Task Diagnostics_CleanSolutionHasNoErrors()
    {
        var diagnostics = await Workspace.GetDocumentDiagnosticsAsync(fixture.PathOf("App/Program.cs"), CancellationToken.None);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverityKind.Error);
    }

    [Fact]
    public async Task UpdateDocumentText_ReportsErrorWithinOneSecond()
    {
        var path = fixture.PathOf("Lib1/Car.cs");
        var original = await File.ReadAllTextAsync(path);
        var received = new TaskCompletionSource<DiagnosticsChangedEventArgs>();
        void Handler(object? sender, DiagnosticsChangedEventArgs e)
        {
            if (e.FilePath == path)
            {
                received.TrySetResult(e);
            }
        }

        Workspace.DiagnosticsChanged += Handler;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Workspace.UpdateDocumentText(path, original.Replace("_engine.Start()", "_engine.Stop()", StringComparison.Ordinal));
            var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopwatch.Stop();

            var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverityKind.Error);
            Assert.Equal("CS1061", error.Id);
            Assert.Equal(11, error.StartLine);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Took {stopwatch.Elapsed}");
        }
        finally
        {
            Workspace.DiagnosticsChanged -= Handler;
            Workspace.UpdateDocumentText(path, original);
        }
    }

    [Fact]
    public async Task Classify_MarksTypesMethodsAndProperties()
    {
        var path = fixture.PathOf("Lib1/Car.cs");
        var text = await File.ReadAllTextAsync(path);

        var spans = await Workspace.ClassifyAsync(path, 0, text.Length, CancellationToken.None);

        string Classification(string token) => spans.First(s => text.Substring(s.Start, s.Length) == token).Classification;
        Assert.Equal(SemanticClassifications.Class, Classification("Engine"));
        Assert.Equal(SemanticClassifications.Method, Classification("Start"));
        Assert.Equal(SemanticClassifications.Property, Classification("Name"));
        Assert.Equal(SemanticClassifications.Field, Classification("_engine"));
    }

    [Fact]
    public async Task QuickInfo_ShowsSignatureAndDocumentationAcrossProjects()
    {
        var path = fixture.PathOf("Lib1/Car.cs");
        var text = await File.ReadAllTextAsync(path);

        var info = await Workspace.GetQuickInfoAsync(path, text.IndexOf("Start()", StringComparison.Ordinal) + 1, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Contains("Engine.Start()", info.Signature, StringComparison.Ordinal);
        Assert.Contains("Starts the engine.", info.Documentation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAll_RaisesSolutionWideEvent()
    {
        var received = new TaskCompletionSource<DiagnosticsChangedEventArgs>();
        void Handler(object? sender, DiagnosticsChangedEventArgs e)
        {
            if (e.FilePath is null)
            {
                received.TrySetResult(e);
            }
        }

        Workspace.DiagnosticsChanged += Handler;
        try
        {
            await Workspace.RefreshAllDiagnosticsAsync(CancellationToken.None);
            var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverityKind.Error);
        }
        finally
        {
            Workspace.DiagnosticsChanged -= Handler;
        }
    }

    [Fact]
    public async Task LooseFile_GetsDiagnosticsWithoutProject()
    {
        var path = Path.Combine(fixture.Root, "Loose.cs");
        Workspace.UpdateDocumentText(path, "class Loose { void M() { int x = \"text\"; } }");

        var diagnostics = await Workspace.GetDocumentDiagnosticsAsync(path, CancellationToken.None);

        Assert.Contains(diagnostics, d => d.Id == "CS0029");
    }
}
