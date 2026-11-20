using Avalonia.Headless.XUnit;
using VsLike.App.Services;
using VsLike.Core.Documents;

namespace VsLike.App.Tests;

public sealed class DocumentManagerTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vslike-docs", Guid.NewGuid().ToString("N"));
    private readonly FakeDialogService _dialogs = new();

    public DocumentManagerTests()
    {
        Directory.CreateDirectory(_folder);
    }

    [AvaloniaFact]
    public async Task Open_ReusesExistingTabAndPreservesBom()
    {
        var path = Path.Combine(_folder, "a.cs");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, (byte)'x']);
        using var workbench = CreateWorkbench();

        await workbench.Documents.OpenAsync(path);
        await workbench.Documents.OpenAsync(path);
        var document = Assert.Single(workbench.Documents.OpenDocuments);
        document.TextDocument.Insert(1, "y");
        await workbench.Documents.SaveAsync(document);

        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'x', (byte)'y'], File.ReadAllBytes(path));
        Assert.False(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task New_ThenSave_AsksForPathAndWritesFile()
    {
        using var workbench = CreateWorkbench();
        var document = workbench.Documents.New();
        document.TextDocument.Insert(0, "hello");
        Assert.True(document.IsDirty);
        Assert.EndsWith("●", document.Title, StringComparison.Ordinal);
        _dialogs.NextSavePath = Path.Combine(_folder, "new.md");

        Assert.True(await workbench.Documents.SaveAsync(document));

        Assert.Equal("hello", File.ReadAllText(_dialogs.NextSavePath));
        Assert.Equal("new.md", document.Title);
    }

    [AvaloniaFact]
    public async Task Close_DirtyDocument_CancelKeepsTabDiscardCloses()
    {
        using var workbench = CreateWorkbench();
        var document = workbench.Documents.New();
        document.TextDocument.Insert(0, "x");

        _dialogs.NextSaveChoice = SaveChangesChoice.Cancel;
        Assert.False(await workbench.Documents.CloseAsync(document));
        Assert.Contains(document, workbench.Documents.OpenDocuments);

        _dialogs.NextSaveChoice = SaveChangesChoice.Discard;
        Assert.True(await workbench.Documents.CloseAsync(document));
        Assert.DoesNotContain(document, workbench.Documents.OpenDocuments);
    }

    [AvaloniaFact]
    public async Task ExternalChange_ReloadsCleanDocumentButNotDirtyOne()
    {
        var clean = Path.Combine(_folder, "clean.cs");
        var dirty = Path.Combine(_folder, "dirty.cs");
        File.WriteAllText(clean, "old");
        File.WriteAllText(dirty, "old");
        using var workbench = CreateWorkbench();
        await workbench.Documents.OpenAsync(clean);
        await workbench.Documents.OpenAsync(dirty);
        var dirtyDocument = workbench.Documents.OpenDocuments.Single(d => d.FilePath == dirty);
        dirtyDocument.TextDocument.Insert(0, "mine ");

        File.WriteAllText(clean, "new");
        File.WriteAllText(dirty, "new");
        await workbench.Documents.OnFilesChangedAsync([clean, dirty]);

        Assert.Equal("new", workbench.Documents.OpenDocuments.Single(d => d.FilePath == clean).TextDocument.Text);
        Assert.Equal("mine old", dirtyDocument.TextDocument.Text);
    }

    [AvaloniaFact]
    public async Task FindInFiles_FindsAndReplacesAcrossFolder()
    {
        File.WriteAllText(Path.Combine(_folder, "a.cs"), "class Foo {}\n");
        File.WriteAllText(Path.Combine(_folder, "b.cs"), "// Foo Foo\n");
        using var workbench = CreateWorkbench();
        workbench.Explorer.OpenFolder(_folder);
        workbench.Search.Query = "Foo";

        await workbench.Search.SearchAsync();
        Assert.Equal(2, workbench.Search.Results.Count);
        Assert.Equal(3, workbench.Search.Results.Sum(r => r.Matches.Count));

        workbench.Search.Replacement = "Bar";
        await workbench.Search.ReplaceAllAsync();
        Assert.Equal("// Bar Bar\n", File.ReadAllText(Path.Combine(_folder, "b.cs")));
    }

    [AvaloniaFact]
    public async Task OpenAtLine_MovesCaretOnceViewIsAttached()
    {
        var path = Path.Combine(_folder, "lines.cs");
        File.WriteAllText(path, "a\nb\nc\n");
        using var workbench = CreateWorkbench();
        var window = new MainWindow(workbench);
        window.Show();

        await workbench.Documents.OpenAsync(path, 3, 1);
        await UiWait.UntilAsync(() => workbench.Documents.Active?.Session is not null, TimeSpan.FromSeconds(5), "editor session");

        Assert.Equal((3, 1), workbench.Documents.Active?.Session?.Caret);
    }

    public void Dispose()
    {
        _platform.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private Workbench CreateWorkbench() => new(new AppServices(_platform), _dialogs, () => null);
}
