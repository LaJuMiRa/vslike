using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using VsLike.Editor;
using VsLike.Editor.Highlighting;

namespace VsLike.App.Editor;

/// <summary>
/// Ansicht eines Editor-Tabs. Die <see cref="EditorSession"/> lebt nur, solange die Ansicht im Visual Tree hängt
/// (Dock erzeugt Ansichten beim Verschieben neu); Text und Undo-Verlauf bleiben im ViewModel.
/// </summary>
public sealed class EditorView : UserControl
{
    private readonly TextEditor _editor;
    private EditorSession? _session;
    private EditorDocumentViewModel? _document;

    public EditorView()
    {
        _editor = new TextEditor
        {
            FontFamily = Application.Current?.TryFindResource("VsMonoFont", out var font) == true && font is FontFamily family ? family : FontFamily.Default,
            FontSize = 13,
            Padding = new Thickness(4, 0, 0, 0),
        };
        Content = _editor;
    }

    public static double EditorFontSize { get; set; } = 13;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnThemeChanged;
        }

        CreateSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnThemeChanged;
        }

        DisposeSession();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
        {
            CreateSession();
        }
    }

    private static EditorThemeKind CurrentThemeKind()
        => Application.Current?.ActualThemeVariant == ThemeVariant.Light ? EditorThemeKind.Light : EditorThemeKind.Dark;

    private void CreateSession()
    {
        if (DataContext is not EditorDocumentViewModel document || (ReferenceEquals(document, _document) && _session is not null))
        {
            return;
        }

        DisposeSession();
        _document = document;
        _editor.FontSize = EditorFontSize;
        _editor.Document = document.TextDocument;
        _session = new EditorSession(_editor, document.FilePath, CurrentThemeKind());
        document.AttachSession(_session);
        _editor.TextArea.Focus();
    }

    private void DisposeSession()
    {
        _document?.AttachSession(null);
        _session?.Dispose();
        _session = null;
        _document = null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _session?.SetTheme(CurrentThemeKind());
}
