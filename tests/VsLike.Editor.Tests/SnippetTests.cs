using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VsLike.Core.Code;
using VsLike.Editor.Highlighting;
using VsLike.Editor.Snippets;

namespace VsLike.Editor.Tests;

public class SnippetTests
{
    [Fact]
    public void Expand_ProducesPlaceholdersAndIndentation()
    {
        var expanded = SnippetExpander.Expand("if (${1:true})\n{\n\t$0\n}", "    ", null);

        Assert.Equal("if (true)\n    {\n        \n    }", expanded.Text);
        var placeholder = Assert.Single(expanded.Placeholders);
        Assert.Equal((1, 4, 4), (placeholder.Index, placeholder.Start, placeholder.Length));
        Assert.Equal("if (true)\n    {\n        ".Length, expanded.FinalCaret);
    }

    [Fact]
    public void Expand_ReplacesTypeVariable()
    {
        Assert.StartsWith("public Car(", SnippetExpander.Expand(SnippetLibrary.Find("ctor")!.Body, string.Empty, "Car").Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void PropTab_ExpandsAndTabMovesToNextPlaceholder()
    {
        var (window, editor, _) = Create("class C\n{\n    prop\n}\n");
        editor.CaretOffset = "class C\n{\n    prop".Length;

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Equal("class C\n{\n    public int MyProperty { get; set; }\n}\n", editor.Text);
        Assert.Equal("int", editor.SelectedText);

        window.KeyTextInput("string");
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Equal("MyProperty", editor.SelectedText);
        window.KeyTextInput("Name");
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.Equal("class C\n{\n    public string Name { get; set; }\n}\n", editor.Text);
    }

    [AvaloniaFact]
    public void ForSnippet_SyncsLinkedPlaceholders()
    {
        var (window, editor, _) = Create("for");
        editor.CaretOffset = 3;

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyTextInput("k");
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.StartsWith("for (var k = 0; k < length; k++)", editor.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Completion_EnterCommitsSelectedEntryWithCurrentSegment()
    {
        var (window, editor, session) = Create("x.Dr");
        editor.CaretOffset = 4;
        (CompletionEntry Entry, int Start, int Length)? committed = null;
        var list = new CompletionList(
            [new CompletionEntry("Drive", "Drive", "Drive", CompletionKind.Method, null, false, new object()), new CompletionEntry("Name", "Name", "Name", CompletionKind.Property, null, false, new object())],
            2,
            2);

        Task Commit(CompletionEntry entry, int start, int length)
        {
            committed = (entry, start, length);
            return Task.CompletedTask;
        }

        session.Completion.Show(list, [], Commit, (_, _, _) => Task.CompletedTask, _ => Task.FromResult<string?>(null));
        Assert.True(session.Completion.IsOpen);
        window.KeyTextInput("i");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.NotNull(committed);
        Assert.Equal("Drive", committed.Value.Entry.DisplayText);
        Assert.Equal((2, 3), (committed.Value.Start, committed.Value.Length));
        Assert.False(session.Completion.IsOpen);
    }

    [AvaloniaFact]
    public void SignatureHelp_ShowsAndTracksActiveParameter()
    {
        var (_, _, session) = Create("M(");
        var help = new SignatureHelp([new SignatureInfo("void M(", ["int a", "string b"], ")", "Docs")], 0, 1);

        session.SignatureHelp.Show(help);

        Assert.True(session.SignatureHelp.IsOpen);
        Assert.Equal(1, session.SignatureHelp.Current!.ActiveParameter);
        session.SignatureHelp.Close();
        Assert.False(session.SignatureHelp.IsOpen);
    }

    private static (Window Window, TextEditor Editor, EditorSession Session) Create(string text)
    {
        var editor = new TextEditor { Document = new TextDocument(text) };
        var window = new Window { Content = editor, Width = 800, Height = 600 };
        window.Show();
        var session = new EditorSession(editor, "Test.cs", EditorThemeKind.Dark);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor, session);
    }
}
