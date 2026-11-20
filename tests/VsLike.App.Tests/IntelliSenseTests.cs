using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VsLike.App.Editor;
using VsLike.App.Services;

namespace VsLike.App.Tests;

public sealed class IntelliSenseTests : IDisposable
{
    private readonly TestPlatform _platform = new();
    private readonly TestSolution _solution = new();
    private readonly FakeDialogService _dialogs = new();

    [AvaloniaFact]
    public async Task Rename_ChangesOpenAndClosedFilesAcrossProjects()
    {
        var (workbench, _) = await OpenAsync();
        var car = await OpenDocumentAsync(workbench, "Lib1/Car.cs");
        _dialogs.NextPrompt = "Motor";

        var offset = car.TextDocument.Text.IndexOf("Engine _engine", StringComparison.Ordinal) + 1;
        var changed = await workbench.Language.RenameAsync(car, offset);

        Assert.Equal(2, changed);
        Assert.Contains("private readonly Motor _engine", car.TextDocument.Text, StringComparison.Ordinal);
        Assert.True(car.IsDirty);
        Assert.Contains("public class Motor", await File.ReadAllTextAsync(_solution.PathOf("Lib2/Engine.cs")), StringComparison.Ordinal);
        workbench.Dispose();
    }

    [AvaloniaFact]
    public async Task TypingDot_OpensCompletionAndTabCommits()
    {
        var (workbench, window) = await OpenAsync();
        var program = await OpenDocumentAsync(workbench, "App/Program.cs");
        var session = program.Session!;
        var offset = program.TextDocument.Text.IndexOf("car.Drive()", StringComparison.Ordinal) + "car".Length;
        program.TextDocument.Remove(offset, ".Drive()".Length);
        session.Editor.CaretOffset = offset;
        session.Editor.TextArea.Focus();

        window.KeyTextInput(".");
        await UiWait.UntilAsync(() => session.Completion.IsOpen, TimeSpan.FromSeconds(10), "completion list");
        Assert.Contains("Drive", session.Completion.VisibleTexts);
        window.KeyTextInput("Dr");
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        await UiWait.UntilAsync(() => program.TextDocument.Text.Contains("car.Drive)", StringComparison.Ordinal), TimeSpan.FromSeconds(5), "committed text");
        workbench.Dispose();
    }

    [AvaloniaFact]
    public async Task QuickFix_AddsMissingUsing()
    {
        var (workbench, _) = await OpenAsync();
        var program = await OpenDocumentAsync(workbench, "App/Program.cs");
        program.TextDocument.Insert(program.TextDocument.TextLength, "var sb = new StringBuilder();\n");
        program.Session!.Editor.CaretOffset = program.TextDocument.Text.IndexOf("StringBuilder", StringComparison.Ordinal) + 2;

        var actions = await workbench.Language.GetCodeActionsAsync(program);
        await workbench.Language.ApplyCodeActionAsync(actions.First(a => a.Title.Contains("using System.Text", StringComparison.Ordinal)));

        Assert.Contains("using System.Text;", program.TextDocument.Text, StringComparison.Ordinal);
        workbench.Dispose();
    }

    public void Dispose()
    {
        _platform.Dispose();
        _solution.Dispose();
    }

    private static async Task<EditorDocumentViewModel> OpenDocumentAsync(Workbench workbench, string relative)
    {
        var path = Path.Combine(Path.GetDirectoryName(workbench.Intelligence.Workspace.SolutionPath)!, relative);
        await workbench.Documents.OpenAsync(path);
        var document = workbench.Documents.Active!;
        await UiWait.UntilAsync(() => document.Session is not null, TimeSpan.FromSeconds(5), "editor session");
        return document;
    }

    private async Task<(Workbench Workbench, MainWindow Window)> OpenAsync()
    {
        var workbench = new Workbench(new AppServices(_platform), _dialogs, () => null);
        var window = new MainWindow(workbench);
        window.Show();
        Assert.True(await workbench.OpenSolutionAsync(_solution.SolutionPath), workbench.Shell.StatusText);
        return (workbench, window);
    }
}
