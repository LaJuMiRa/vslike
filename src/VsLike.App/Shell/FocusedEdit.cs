using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

namespace VsLike.App.Shell;

/// <summary>
/// Leitet Bearbeiten-Befehle (Rückgängig, Kopieren …) an das fokussierte Steuerelement weiter. Nötig, weil die
/// native macOS-Menüleiste ⌘C/⌘Z abfängt, bevor die Taste das Steuerelement erreicht.
/// </summary>
public static class FocusedEdit
{
    public static bool Run(TopLevel? topLevel, EditAction action)
    {
        var focused = topLevel?.FocusManager?.GetFocusedElement();
        if (focused is TextArea area && area.FindAncestorOfType<TextEditor>() is { } editor)
        {
            Apply(editor, action);
            return true;
        }

        if (focused is TextBox textBox)
        {
            Apply(textBox, action);
            return true;
        }

        return false;
    }

    private static void Apply(TextEditor editor, EditAction action)
    {
        switch (action)
        {
            case EditAction.Undo: editor.Undo(); break;
            case EditAction.Redo: editor.Redo(); break;
            case EditAction.Cut: editor.Cut(); break;
            case EditAction.Copy: editor.Copy(); break;
            case EditAction.Paste: editor.Paste(); break;
            case EditAction.SelectAll: editor.SelectAll(); break;
        }
    }

    private static void Apply(TextBox textBox, EditAction action)
    {
        switch (action)
        {
            case EditAction.Undo: textBox.Undo(); break;
            case EditAction.Redo: textBox.Redo(); break;
            case EditAction.Cut: textBox.Cut(); break;
            case EditAction.Copy: textBox.Copy(); break;
            case EditAction.Paste: textBox.Paste(); break;
            case EditAction.SelectAll: textBox.SelectAll(); break;
        }
    }
}
