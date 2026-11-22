using VsLike.Core.Build;
using VsLike.Core.Code;
using VsLike.Roslyn.Analyzers;

namespace VsLike.Roslyn.Tests;

/// <summary>Abnahme Phase 7: StyleCop an/aus, Konfiguration über stylecop.json und .editorconfig, Code-Fixes inkl. Fix all.</summary>
public sealed class StyleCopTests
{
    private static readonly IDotnetCli NoCli = new UnusedCli();

    [Fact]
    public void Package_IsInCache_AndLoadsAnalyzersAndFixes()
    {
        Assert.True(StyleCopPackage.IsInCache, StyleCopPackage.AnalyzerDirectory);
        Assert.True(StyleCopAnalyzers.Analyzers.Length > 50);
        Assert.Contains(StyleCopAnalyzers.FixProviders, p => p.FixableDiagnosticIds.Contains("SA1028"));
    }

    [Fact]
    public async Task Toggle_ShowsStyleCopDiagnosticsOnlyWhenEnabled()
    {
        var fixture = await LoadAsync();
        try
        {
            var car = fixture.PathOf("Lib1/Car.cs");
            Assert.DoesNotContain(await fixture.Workspace.GetDocumentDiagnosticsAsync(car, CancellationToken.None), d => d.Category == "StyleCop");

            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));
            var on = await fixture.Workspace.GetDocumentDiagnosticsAsync(car, CancellationToken.None);
            Assert.Contains(on, d => d.Id == "SA1200" && d.Category == "StyleCop");
            Assert.Contains(on, d => d.Id == "SA1633");

            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(false, NoCli, CancellationToken.None));
            Assert.DoesNotContain(await fixture.Workspace.GetDocumentDiagnosticsAsync(car, CancellationToken.None), d => d.Category == "StyleCop");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task StyleCopJsonAndEditorConfig_AreRespectedWithoutChangingProjects()
    {
        var fixture = await LoadAsync(root =>
        {
            File.WriteAllText(Path.Combine(root, "stylecop.json"), """{ "settings": { "orderingRules": { "usingDirectivesPlacement": "outsideNamespace" } } }""");
            File.WriteAllText(Path.Combine(root, ".editorconfig"), "root = true\n\n[*.cs]\ndotnet_diagnostic.SA1633.severity = none\n");
        });
        try
        {
            var projectText = File.ReadAllText(fixture.PathOf("Lib1/Lib1.csproj"));
            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));

            var diagnostics = await fixture.Workspace.GetDocumentDiagnosticsAsync(fixture.PathOf("Lib1/Car.cs"), CancellationToken.None);
            Assert.Contains(diagnostics, d => d.Category == "StyleCop");
            Assert.DoesNotContain(diagnostics, d => d.Id == "SA1200");
            Assert.DoesNotContain(diagnostics, d => d.Id == "SA1633");
            Assert.Equal(projectText, File.ReadAllText(fixture.PathOf("Lib1/Lib1.csproj")));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task EnablingAgain_RereadsAnEditedStyleCopJson()
    {
        var fixture = await LoadAsync(root => File.WriteAllText(Path.Combine(root, "stylecop.json"), """{ "settings": { } }"""));
        try
        {
            var car = fixture.PathOf("Lib1/Car.cs");
            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));
            Assert.Contains(await fixture.Workspace.GetDocumentDiagnosticsAsync(car, CancellationToken.None), d => d.Id == "SA1200");

            File.WriteAllText(Path.Combine(fixture.Root, "stylecop.json"), """{ "settings": { "orderingRules": { "usingDirectivesPlacement": "outsideNamespace" } } }""");
            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));

            Assert.DoesNotContain(await fixture.Workspace.GetDocumentDiagnosticsAsync(car, CancellationToken.None), d => d.Id == "SA1200");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task CodeFix_AndFixAllInDocument_RemoveTrailingWhitespace()
    {
        VsLike.Core.Localization.EngineText.SetCulture("en");
        var fixture = await LoadAsync();
        try
        {
            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));
            var path = fixture.PathOf("Lib2/Engine.cs");
            var text = File.ReadAllText(path).Replace("{\n", "{   \n", StringComparison.Ordinal).Replace("= 100;\n", "= 100;  \n", StringComparison.Ordinal);
            fixture.Workspace.UpdateDocumentText(path, text);
            var features = new RoslynLanguageFeatures(fixture.Workspace);

            var actions = await features.GetCodeActionsAsync(path, text.IndexOf("{   ", StringComparison.Ordinal) + 1, 0, CancellationToken.None);
            Assert.Contains(actions, a => a.IsFix && a.Group == "SA1028" && !a.Title.Contains("Fix all", StringComparison.Ordinal));
            var fixAll = Assert.Single(actions, a => a.Group == "SA1028" && a.Title.StartsWith("Fix all in document", StringComparison.Ordinal));
            Assert.Contains(actions, a => a.Group == "SA1028" && a.Title.StartsWith("Fix all in project", StringComparison.Ordinal));

            var edit = await features.GetCodeActionEditAsync(fixAll, CancellationToken.None);
            var fixedText = Apply(text, Assert.Single(edit.Files).Edits);
            Assert.DoesNotContain("{   \n", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("= 100;  \n", fixedText, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task DocumentationFix_AddsStyleCopConformingComments_AndFixAllDocumentsEveryElement()
    {
        VsLike.Core.Localization.EngineText.SetCulture("en");
        var fixture = await LoadAsync();
        try
        {
            Assert.True(await fixture.Workspace.SetStyleCopEnabledAsync(true, NoCli, CancellationToken.None));
            var path = fixture.PathOf("Lib2/Engine.cs");
            const string text = """
                namespace Lib2;

                public sealed class Engine : IDisposable
                {
                    public Engine(int horsepower)
                    {
                        Horsepower = horsepower;
                    }

                    public int Horsepower { get; set; }

                    public bool IsRunning { get; }

                    public T Convert<T>(string value) => default!;

                    public bool IsValid(int size) => size > 0;

                    public void Dispose()
                    {
                    }

                    public override string ToString() => "engine";
                }

                """;
            fixture.Workspace.UpdateDocumentText(path, text);
            var features = new RoslynLanguageFeatures(fixture.Workspace);

            var single = await features.GetCodeActionsAsync(path, text.IndexOf("Horsepower {", StringComparison.Ordinal), 0, CancellationToken.None);
            var add = Assert.Single(single, a => a.Group == "SA1600" && a.Title == "Add documentation comment");
            var singleEdit = Assert.Single((await features.GetCodeActionEditAsync(add, CancellationToken.None)).Files);
            Assert.Contains("    /// <summary>\n    /// Gets or sets the horsepower.\n    /// </summary>\n    public int Horsepower", Apply(text, singleEdit.Edits), StringComparison.Ordinal);

            var fixAll = Assert.Single(single, a => a.Group == "SA1600" && a.Title == "Fix all in document: Add documentation comment");
            var fixedText = Apply(text, Assert.Single((await features.GetCodeActionEditAsync(fixAll, CancellationToken.None)).Files).Edits);
            Assert.Contains("/// Initializes a new instance of the <see cref=\"Engine\"/> class.", fixedText, StringComparison.Ordinal);
            Assert.Contains("/// <param name=\"horsepower\">The horsepower.</param>", fixedText, StringComparison.Ordinal);
            Assert.Contains("/// Gets a value indicating whether running.", fixedText, StringComparison.Ordinal);
            Assert.Contains("/// <typeparam name=\"T\">The type of the value.</typeparam>", fixedText, StringComparison.Ordinal);
            Assert.Contains("/// <returns><c>true</c> if valid; otherwise, <c>false</c>.</returns>", fixedText, StringComparison.Ordinal);
            Assert.Contains("    /// <inheritdoc/>\n    public void Dispose()", fixedText, StringComparison.Ordinal);
            Assert.Contains("    /// <inheritdoc/>\n    public override string ToString()", fixedText, StringComparison.Ordinal);
            Assert.Contains("/// Represents an engine.", fixedText, StringComparison.Ordinal);

            fixture.Workspace.UpdateDocumentText(path, fixedText);
            var after = await fixture.Workspace.GetDocumentDiagnosticsAsync(path, CancellationToken.None);
            Assert.DoesNotContain(after, d => d.Id is "SA1600" or "SA1623" or "SA1642" or "SA1629" or "SA1611" or "SA1615" or "SA1618" or "CS1570");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static string Apply(string text, IReadOnlyList<TextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        }

        return text;
    }

    private static async Task<SampleSolutionFixture> LoadAsync(Action<string>? beforeLoad = null)
    {
        var fixture = new SampleSolutionFixture { BeforeLoad = beforeLoad };
        await fixture.InitializeAsync();
        return fixture;
    }

    private sealed class UnusedCli : IDotnetCli
    {
        public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<OutputLine>? onOutput, CancellationToken cancellationToken)
            => throw new InvalidOperationException("StyleCop should come from the NuGet cache in tests.");
    }
}
