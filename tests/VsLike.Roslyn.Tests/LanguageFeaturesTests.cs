using VsLike.Core.Code;

namespace VsLike.Roslyn.Tests;

[Collection(SampleSolutionCollection.Name)]
public sealed class LanguageFeaturesTests(SampleSolutionFixture fixture) : IDisposable
{
    private RoslynLanguageFeatures Features => new(fixture.Workspace);

    [Fact]
    public async Task Completion_AfterDot_ListsMembers()
    {
        var (path, offset) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "Console.WriteLine(car.);", "car.");

        var list = await Features.GetCompletionAsync(path, offset, CancellationToken.None);

        Assert.NotNull(list);
        Assert.Contains(list.Items, i => i.DisplayText == "Drive" && i.Kind == CompletionKind.Method);
        Assert.Contains(list.Items, i => i.DisplayText == "Name" && i.Kind == CompletionKind.Property);
        Assert.True(await Features.ShouldTriggerCompletionAsync(path, offset, '.', CancellationToken.None));
    }

    [Fact]
    public async Task Completion_UnimportedType_AddsUsingOnCommit()
    {
        var (path, offset) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "StringBu", "StringBu", afterNeedle: true);

        var list = await Features.GetCompletionAsync(path, offset, CancellationToken.None);
        var entry = Assert.Single(list!.Items, i => i.DisplayText == "StringBuilder" && i.InlineDescription == "System.Text");
        var edit = await Features.ResolveCompletionAsync(path, entry, CancellationToken.None);

        var result = Apply(await File.ReadAllTextAsync(path), "Console.WriteLine(car.Drive());", "StringBu", edit.Files.Single().Edits);
        Assert.StartsWith("using System.Text;", result, StringComparison.Ordinal);
        Assert.Contains("StringBuilder", result, StringComparison.Ordinal);
        Assert.NotNull(await Features.GetCompletionDescriptionAsync(path, entry, CancellationToken.None));
    }

    [Fact]
    public async Task Completion_WarmLatencyIsBelow150Milliseconds()
    {
        var (path, offset) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "Console.WriteLine(car.);", "car.");
        await Features.GetCompletionAsync(path, offset, CancellationToken.None);
        var timings = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            fixture.Workspace.UpdateDocumentText(path, (await File.ReadAllTextAsync(path)).Replace("Console.WriteLine(car.Drive());", "Console.WriteLine(car.);" + new string(' ', i), StringComparison.Ordinal));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var list = await Features.GetCompletionAsync(path, offset, CancellationToken.None);
            stopwatch.Stop();
            Assert.NotNull(list);
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var median = timings.Order().ElementAt(timings.Count / 2);
        Assert.True(median < 150, $"Median {median:0} ms (all: {string.Join(", ", timings.Select(t => t.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))})");
    }

    [Fact]
    public async Task SignatureHelp_ShowsOverloadsAndActiveParameter()
    {
        var (path, offset) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "Console.WriteLine(\"{0} {1}\", 1, ", "1, ", afterNeedle: true);

        var help = await Features.GetSignatureHelpAsync(path, offset, CancellationToken.None);

        Assert.NotNull(help);
        Assert.True(help.Signatures.Count > 5);
        Assert.Equal(2, help.ActiveParameter);
        Assert.All(help.Signatures, s => Assert.Contains("WriteLine(", s.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoToDefinition_JumpsAcrossProjects()
    {
        var path = fixture.PathOf("App/Program.cs");
        var text = await File.ReadAllTextAsync(path);
        fixture.Workspace.UpdateDocumentText(path, text);

        var locations = await Features.GoToDefinitionAsync(path, text.IndexOf("Drive", StringComparison.Ordinal) + 1, CancellationToken.None);

        var location = Assert.Single(locations);
        Assert.EndsWith("Car.cs", location.FilePath, StringComparison.Ordinal);
        Assert.Equal(11, location.Line);
    }

    [Fact]
    public async Task FindReferences_ReturnsDefinitionAndUsages()
    {
        var path = fixture.PathOf("Lib2/Engine.cs");
        var text = await File.ReadAllTextAsync(path);

        var references = await Features.FindReferencesAsync(path, text.IndexOf("Start()", StringComparison.Ordinal), CancellationToken.None);

        Assert.Equal(2, references.Count);
        Assert.Contains(references, r => r.FilePath.EndsWith("Car.cs", StringComparison.Ordinal) && r.LineText.Contains("_engine.Start()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rename_ChangesAllFilesAcrossProjects()
    {
        var path = fixture.PathOf("Lib2/Engine.cs");
        var text = await File.ReadAllTextAsync(path);
        var offset = text.IndexOf("class Engine", StringComparison.Ordinal) + "class ".Length;

        var target = await Features.PrepareRenameAsync(path, offset, CancellationToken.None);
        var edit = await Features.RenameAsync(path, offset, "Motor", CancellationToken.None);

        Assert.NotNull(target);
        Assert.True(target.CanRename);
        Assert.Equal("Engine", target.CurrentName);
        Assert.Equal(["Car.cs", "Engine.cs"], edit.Files.Select(f => Path.GetFileName(f.FilePath)).Order());
        var car = edit.Files.Single(f => f.FilePath.EndsWith("Car.cs", StringComparison.Ordinal));
        Assert.All(car.Edits, e => Assert.Equal("Motor", e.NewText));
    }

    [Fact]
    public async Task CodeActions_OfferAddUsingForMissingType()
    {
        var (path, offset) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "var sb = new StringBuilder();", "StringBuilder");

        var actions = await Features.GetCodeActionsAsync(path, offset, 0, CancellationToken.None);
        var addUsing = actions.FirstOrDefault(a => a.IsFix && a.Title.Contains("using System.Text", StringComparison.Ordinal));

        Assert.True(addUsing is not null, string.Join(" | ", actions.Select(a => a.Title)));
        var edit = await Features.GetCodeActionEditAsync(addUsing, CancellationToken.None);
        var fixedText = Apply(await File.ReadAllTextAsync(path), "Console.WriteLine(car.Drive());", "var sb = new StringBuilder();", edit.Files.Single().Edits);
        Assert.Contains("using System.Text;", fixedText, StringComparison.Ordinal);
        Assert.True(CodeActionProviderCounts.Fixes > 100);
    }

    [Fact]
    public async Task FormatDocument_FixesIndentation()
    {
        var (path, _) = await WithTextAsync("App/Program.cs", "Console.WriteLine(car.Drive());", "if (car is not null)\n{\nConsole.WriteLine(car.Drive());\n      }", "if");

        var edit = await Features.FormatDocumentAsync(path, CancellationToken.None);

        var formatted = Apply(await File.ReadAllTextAsync(path), "Console.WriteLine(car.Drive());", "if (car is not null)\n{\nConsole.WriteLine(car.Drive());\n      }", edit.Files.Single().Edits);
        Assert.Contains("if (car is not null)\n{\n    Console.WriteLine(car.Drive());\n}", formatted.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnclosingTypeName_FindsClass()
    {
        var path = fixture.PathOf("Lib1/Car.cs");
        var text = await File.ReadAllTextAsync(path);

        Assert.Equal("Car", await Features.GetEnclosingTypeNameAsync(path, text.IndexOf("Drive", StringComparison.Ordinal), CancellationToken.None));
    }

    public void Dispose()
    {
        foreach (var file in new[] { "App/Program.cs", "Lib1/Car.cs", "Lib2/Engine.cs" })
        {
            fixture.Workspace.UpdateDocumentText(fixture.PathOf(file), File.ReadAllText(fixture.PathOf(file)));
        }
    }

    private static string Apply(string original, string search, string replacement, IReadOnlyList<TextEdit> edits)
    {
        var text = original.Replace(search, replacement, StringComparison.Ordinal);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        }

        return text;
    }

    /// <summary>Replaces <paramref name="search"/> in a file (in memory only) and returns the offset at/after <paramref name="needle"/>.</summary>
    private async Task<(string Path, int Offset)> WithTextAsync(string relative, string search, string replacement, string needle, bool afterNeedle = true)
    {
        var path = fixture.PathOf(relative);
        var text = (await File.ReadAllTextAsync(path)).Replace(search, replacement, StringComparison.Ordinal);
        fixture.Workspace.UpdateDocumentText(path, text);
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        return (path, afterNeedle ? index + needle.Length : index);
    }
}
