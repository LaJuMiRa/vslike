using System.Text;
using AvaloniaEdit.Document;
using Dock.Model.Mvvm.Controls;
using VsLike.Core.Localization;
using VsLike.Editor;
using VsLike.Editor.Highlighting;

namespace VsLike.App.Editor;

/// <summary>Ein geöffneter Editor-Tab. Der Text lebt im <see cref="TextDocument"/> (inkl. Undo-Stack).</summary>
public sealed class EditorDocumentViewModel : Document
{
    private readonly int _untitledNumber;
    private bool _wasDirty;

    public EditorDocumentViewModel(string? filePath, string text, Encoding encoding, int untitledNumber = 0)
    {
        FilePath = filePath;
        Encoding = encoding;
        _untitledNumber = untitledNumber;
        TextDocument = new TextDocument(text) { FileName = filePath };
        Id = filePath ?? $"untitled:{untitledNumber}";
        TextDocument.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
            {
                UpdateTitle();
            }
        };
        Loc.CultureChanged += (_, _) => UpdateTitle();
        UpdateTitle();
    }

    /// <summary>Raised when the editor view creates or drops its session.</summary>
    public event EventHandler? SessionChanged;

    public string? FilePath { get; private set; }

    public Encoding Encoding { get; }

    public TextDocument TextDocument { get; }

    public bool IsDirty => !TextDocument.UndoStack.IsOriginalFile || (FilePath is null && TextDocument.TextLength > 0);

    public string DisplayName => FilePath is null ? Loc.Format("Document.Untitled", _untitledNumber) : Path.GetFileName(FilePath);

    public EditorLanguage Language => LanguageRegistry.FromPath(FilePath);

    /// <summary>Gets the live editor session while the view is attached.</summary>
    public EditorSession? Session { get; private set; }

    /// <summary>Gets or sets a pending caret position applied when the view attaches.</summary>
    public (int Line, int Column)? PendingNavigation { get; set; }

    public void AttachSession(EditorSession? session)
    {
        Session = session;
        if (session is not null && PendingNavigation is { } target)
        {
            PendingNavigation = null;
            session.GoTo(target.Line, target.Column);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        Id = path;
        TextDocument.FileName = path;
        TextDocument.UndoStack.MarkAsOriginalFile();
        UpdateTitle();
    }

    /// <summary>Replaces the text after an external change, keeping it undoable.</summary>
    public void ReloadText(string text)
    {
        TextDocument.Replace(0, TextDocument.TextLength, text);
        TextDocument.UndoStack.MarkAsOriginalFile();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var dirty = IsDirty;
        Title = dirty ? $"{DisplayName} ●" : DisplayName;
        IsModified = dirty;
        if (dirty != _wasDirty)
        {
            _wasDirty = dirty;
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
