using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>
/// Keeps one editor per open document so that switching tabs keeps scroll position, selection and undo.
/// </summary>
public sealed class EditorRegistry
{
    private readonly Dictionary<Guid, CodeTextView> _editors = [];

    public static EditorRegistry Shared { get; } = new();

    public CodeTextView Editor(CodeDocument document, Workspace workspace)
    {
        if (_editors.TryGetValue(document.Id, out var existing))
        {
            return existing;
        }

        var created = CodeTextView.Make(document, workspace);
        _editors[document.Id] = created;
        return created;
    }

    public void Prune(IEnumerable<CodeDocument> documents)
    {
        var alive = documents.Select(d => d.Id).ToHashSet();
        foreach (var id in _editors.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _editors[id].Detach();
            _editors.Remove(id);
        }
    }
}

/// <summary>
/// Hosts the editor of a document. Diagnostics, execution line and pending navigation come in as values like in the
/// SwiftUI original (<see cref="Update"/>); the host also follows later changes of the workspace, the document and the
/// preferences by itself, because Avalonia does not re-create views when observed values change.
/// </summary>
public sealed class EditorHost : Decorator
{
    private readonly CodeDocument _document;
    private readonly Workspace _workspace;

    public EditorHost(CodeDocument document, Workspace workspace, IReadOnlyList<Diagnostic> diagnostics, int? executionLine, TextRange? pendingSelection, double fontSize)
    {
        _document = document;
        _workspace = workspace;
        ClipToBounds = true;
        Attach();
        Update(diagnostics, executionLine, pendingSelection, fontSize);
    }

    /// <summary>Gets the editor shown by this host.</summary>
    public CodeTextView TextView => EditorRegistry.Shared.Editor(_document, _workspace);

    /// <summary>Applies new values (SwiftUI's <c>updateNSView</c>).</summary>
    public void Update(IReadOnlyList<Diagnostic> diagnostics, int? executionLine, TextRange? pendingSelection, double fontSize)
    {
        _ = executionLine;
        _ = fontSize;
        var text = TextView;
        if (text.Parent != this)
        {
            Attach();
        }

        text.ApplyFont();
        text.SetDiagnostics(diagnostics);
        text.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        text.Gutter.InvalidateVisual();
        if (pendingSelection is { } selection)
        {
            _document.PendingSelection = null;
            Dispatcher.UIThread.Post(() =>
            {
                text.TextArea.Focus();
                text.Reveal(selection);
            });
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _workspace.Diagnostics.PropertyChanged += Changed;
        _workspace.Preferences.PropertyChanged += Changed;
        _document.PropertyChanged += Changed;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _workspace.Diagnostics.PropertyChanged -= Changed;
        _workspace.Preferences.PropertyChanged -= Changed;
        _document.PropertyChanged -= Changed;
        base.OnDetachedFromVisualTree(e);
    }

    private void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh() => Update(
        _workspace.Diagnostics.DiagnosticsFor(_document.Path),
        _document.ExecutionLine,
        _document.PendingSelection,
        _workspace.Preferences.FontSize);

    private void Attach()
    {
        var text = TextView;
        if (text.Parent is Decorator previous && previous != this)
        {
            previous.Child = null;
        }

        Child = text;
        _workspace.ActiveEditor = text;
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(text)?.FocusManager?.GetFocusedElement() is not TextArea)
            {
                text.TextArea.Focus();
            }
        });
    }
}

/// <summary>The editor area for one code document, with a breadcrumb bar above the text.</summary>
public sealed class CodeEditorPane : DockPanel
{
    public CodeEditorPane(CodeDocument document, Workspace workspace)
    {
        Background = Palette.EditorBackground.Brush;
        var breadcrumb = new Breadcrumb(document, workspace);
        var divider = new Border { Height = 1, Background = Palette.Separator.Brush, Opacity = 0.5 };
        SetDock(breadcrumb, Dock.Top);
        SetDock(divider, Dock.Top);
        Children.Add(breadcrumb);
        Children.Add(divider);
        Children.Add(new EditorHost(
            document,
            workspace,
            workspace.Diagnostics.DiagnosticsFor(document.Path),
            document.ExecutionLine,
            document.PendingSelection,
            workspace.Preferences.FontSize));
        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) =>
            {
                Background = Palette.EditorBackground.Brush;
                divider.Background = Palette.Separator.Brush;
            };
        }
    }
}

/// <summary>Project › folders › file, plus diagnostics counts and the language of the file.</summary>
public sealed class Breadcrumb : DockPanel
{
    private readonly CodeDocument _document;
    private readonly Workspace _workspace;

    public Breadcrumb(CodeDocument document, Workspace workspace)
    {
        _document = document;
        _workspace = workspace;
        Height = 26;
        Margin = new Thickness(12, 0);
        Build();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _workspace.Diagnostics.PropertyChanged += Changed;
        _document.PropertyChanged += Changed;
        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _workspace.Diagnostics.PropertyChanged -= Changed;
        _document.PropertyChanged -= Changed;
        base.OnDetachedFromVisualTree(e);
    }

    private static TextBlock Label(string text, IBrush brush) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private IReadOnlyList<string> Parts()
    {
        if (_workspace.RootPath is not { } root)
        {
            return [_document.Name];
        }

        var relative = _document.Path.StartsWith(root + "/", StringComparison.Ordinal) ? _document.Path[(root.Length + 1)..] : _document.Name;
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagnosticsStore.Live) or nameof(DiagnosticsStore.Build) or nameof(CodeDocument.Path) or null)
        {
            Build();
        }
    }

    private void Build()
    {
        Children.Clear();
        var problems = _workspace.Diagnostics.DiagnosticsFor(_document.Path);
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        var errors = problems.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = problems.Count(d => d.Severity == DiagnosticSeverity.Warning);
        if (errors > 0)
        {
            trailing.Children.Add(Count("xmark.octagon.fill", errors, Palette.Error.Current));
        }

        if (warnings > 0)
        {
            trailing.Children.Add(Count("exclamationmark.triangle.fill", warnings, Palette.Warning.Current));
        }

        trailing.Children.Add(Label(_document.LanguageName, Palette.SecondaryLabel.Brush));
        SetDock(trailing, Dock.Right);
        Children.Add(trailing);

        var path = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var parts = Parts();
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                path.Children.Add(EditorGlyphs.Icon("chevron.compact.right", Palette.TertiaryLabel.Current, 9));
            }

            path.Children.Add(Label(parts[index], index == parts.Count - 1 ? Palette.Label.Brush : Palette.SecondaryLabel.Brush));
        }

        Children.Add(path);

        static Control Count(string symbol, int count, Color color)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            label.Children.Add(EditorGlyphs.Icon(symbol, color, 11));
            label.Children.Add(Label(count.ToString(System.Globalization.CultureInfo.InvariantCulture), new SolidColorBrush(color)));
            return label;
        }
    }
}
