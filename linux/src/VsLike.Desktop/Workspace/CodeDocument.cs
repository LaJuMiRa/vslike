using System.Text;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>
/// An open text file. The text lives in an AvaloniaEdit <see cref="TextDocument"/> (<see cref="Storage"/>) shared with the
/// editor view (NSTextStorage on macOS); undo is per document through its <see cref="UndoStack"/>.
/// </summary>
public sealed partial class CodeDocument : ObservableObject
{
    private int _savedVersion;

    public CodeDocument(string path, string text)
    {
        Path = path;
        Storage = new TextDocument(text) { FileName = path };
        Storage.UndoStack.ClearAll();
        Storage.TextChanged += (_, _) => TextDidChange();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public TextDocument Storage { get; }

    public UndoStack UndoStack => Storage.UndoStack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(IsCSharp), nameof(LanguageName))]
    public partial string Path { get; private set; }

    /// <summary>Gets the counter bumped on every text change (the editor and the engine sync react to it).</summary>
    [ObservableProperty]
    public partial int Version { get; private set; }

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyDictionary<int, SourceBreakpoint> Breakpoints { get; set; } = new Dictionary<int, SourceBreakpoint>();

    /// <summary>Gets or sets the 1-based line where the debugger is stopped in this file.</summary>
    [ObservableProperty]
    public partial int? ExecutionLine { get; set; }

    /// <summary>Gets or sets a pending caret target applied when the editor shows the document.</summary>
    [ObservableProperty]
    public partial TextRange? PendingSelection { get; set; }

    public Action<CodeDocument>? OnChange { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "/";

    public string Text => Storage.Text;

    public bool IsCSharp => Extension == "cs";

    public string LanguageName => Extension switch
    {
        "cs" => "C#",
        "csproj" or "props" or "targets" or "xml" or "slnx" or "config" or "resx" or "xaml" or "axaml" => "XML",
        "json" => "JSON",
        "md" => "Markdown",
        _ => L.T("Plain Text"),
    };

    private string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToLowerInvariant();

    public static CodeDocument Load(string path)
    {
        var data = File.ReadAllBytes(path);
        var text = new UTF8Encoding(false, false).GetString(data);
        return new CodeDocument(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Called after every edit (typing, undo, programmatic changes) through <see cref="TextDocument.TextChanged"/>.</summary>
    public void TextDidChange()
    {
        Version++;
        IsDirty = Version != _savedVersion;
        OnChange?.Invoke(this);
    }

    public void Save()
    {
        File.WriteAllText(Path, Text, new UTF8Encoding(false));
        _savedVersion = Version;
        IsDirty = false;
    }

    public void Rename(string newPath)
    {
        Path = newPath;
        Storage.FileName = newPath;
    }

    /// <summary>Replaces the whole text after an external change on disk (kept undoable).</summary>
    public void Reload(string disk)
    {
        if (disk == Text)
        {
            return;
        }

        Replace(new TextRange(0, Storage.TextLength), disk);
        _savedVersion = Version;
        IsDirty = false;
    }

    /// <summary>An undoable replacement outside of typing (code fixes, rename, diagram changes, formatting).</summary>
    /// <param name="actionName">Kept for parity with macOS; AvaloniaEdit's undo stack has no action names.</param>
    public void Replace(TextRange range, string replacement, string? actionName = null)
    {
        _ = actionName;
        var location = Math.Min(range.Location, Storage.TextLength);
        var length = Math.Min(range.Length, Math.Max(0, Storage.TextLength - location));
        Storage.Replace(location, length, replacement);
    }

    /// <summary>Applies several edits as one undo step (offsets refer to the text before the edits).</summary>
    public void Apply(IEnumerable<TextEdit> edits, string actionName)
    {
        Storage.BeginUpdate();
        try
        {
            foreach (var edit in edits.OrderByDescending(e => e.Start))
            {
                Replace(new TextRange(edit.Start, edit.Length), edit.NewText, actionName);
            }
        }
        finally
        {
            Storage.EndUpdate();
        }
    }

    /// <summary>UTF-16 offset of a 1-based line and column.</summary>
    public int Offset(int line, int column)
    {
        var text = Text;
        var current = 1;
        var index = 0;
        while (current < line && index < text.Length)
        {
            index = TextEditing.LineRange(text, index).End;
            current++;
        }

        return Math.Min(text.Length, index + Math.Max(0, column - 1));
    }

    /// <summary>1-based line and column of an offset.</summary>
    public (int Line, int Column) Position(int offset)
    {
        var text = Text;
        var clamped = Math.Min(offset, text.Length);
        var line = 1;
        var index = 0;
        while (index < clamped)
        {
            var end = TextEditing.LineRange(text, index).End;
            if (end > clamped)
            {
                break;
            }

            index = end;
            line++;
        }

        return (line, clamped - index + 1);
    }

    public void ToggleBreakpoint(int line)
    {
        var updated = new Dictionary<int, SourceBreakpoint>(Breakpoints);
        if (!updated.Remove(line))
        {
            updated[line] = new SourceBreakpoint(line);
        }

        Breakpoints = updated;
    }
}
