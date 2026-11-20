using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VsLike.Editor.Highlighting;

namespace VsLike.Editor.Tests;

public class EditorSessionTests
{
    [AvaloniaFact]
    public void Typing_AutoClosesAndOvertypesBrackets()
    {
        var (window, editor, _) = Create("Test.cs", string.Empty);

        window.KeyTextInput("M");
        window.KeyTextInput("(");
        Assert.Equal("M()", editor.Text);

        window.KeyTextInput(")");
        Assert.Equal("M()", editor.Text);
        Assert.Equal(3, editor.CaretOffset);
    }

    [AvaloniaFact]
    public void Backspace_DeletesEmptyPair()
    {
        var (window, editor, _) = Create("Test.cs", string.Empty);
        window.KeyTextInput("[");

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(string.Empty, editor.Text);
    }

    [AvaloniaFact]
    public void ToggleComment_CommentsCaretLine()
    {
        var (_, editor, session) = Create("Test.cs", "a();\nb();\n");
        editor.CaretOffset = 6;

        session.ToggleComment();

        Assert.Equal("a();\n// b();\n", editor.Text);
    }

    [AvaloniaFact]
    public void MultiCaret_TypesAtAllCaretsAsOneUndoStep()
    {
        var (window, editor, session) = Create("Test.cs", "foo\nfoo\nfoo\n");
        editor.CaretOffset = 1;

        session.MultiCaret.AddNextOccurrence();
        session.MultiCaret.AddNextOccurrence();
        session.MultiCaret.AddNextOccurrence();
        window.KeyTextInput("x");

        Assert.Equal("x\nx\nx\n", editor.Text);
        editor.Undo();
        Assert.Equal("foo\nfoo\nfoo\n", editor.Text);
    }

    [AvaloniaFact]
    public void UndoRedo_RestoresText()
    {
        var (window, editor, _) = Create("notes.md", "a");
        editor.CaretOffset = 1;
        window.KeyTextInput("b");

        editor.Undo();
        Assert.Equal("a", editor.Text);
        editor.Redo();
        Assert.Equal("ab", editor.Text);
    }

    [AvaloniaFact]
    public void GoTo_MovesCaretToLineAndColumn()
    {
        var (_, editor, session) = Create("Test.cs", "one\ntwo\nthree\n");

        session.GoTo(3, 2);

        Assert.Equal(new TextLocation(3, 2), editor.Document.GetLocation(editor.CaretOffset));
    }

    [AvaloniaFact]
    public void LargeFile_With50kLines_LoadsAndRendersQuickly()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 50_000; i++)
        {
            builder.Append("    public int Property").Append(i).Append(" { get; set; } // comment ").Append(i).Append('\n');
        }

        var stopwatch = Stopwatch.StartNew();
        var (window, editor, session) = Create("Big.cs", "class Big\n{\n" + builder + "}\n");
        session.GoTo(49_000);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        stopwatch.Stop();

        Assert.Equal(50_004, editor.Document.LineCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took {stopwatch.Elapsed}");
    }

    private static (Window Window, TextEditor Editor, EditorSession Session) Create(string path, string text)
    {
        var editor = new TextEditor { Document = new TextDocument(text) };
        var window = new Window { Content = editor, Width = 800, Height = 600 };
        window.Show();
        var session = new EditorSession(editor, path, EditorThemeKind.Dark);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor, session);
    }
}
