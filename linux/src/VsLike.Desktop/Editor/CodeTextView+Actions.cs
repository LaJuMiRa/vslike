using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VsLike.Kit;

namespace VsLike.Desktop.Editor;

/// <summary>Quick actions (Ctrl+Shift+.), navigation, refactoring and breakpoints of the code editor.</summary>
public sealed partial class CodeTextView
{
    // MARK: Quick actions (Ctrl+Shift+.)
    public void ShowQuickActions()
    {
        if (Document is not { } document)
        {
            return;
        }

        DismissCompletion();
        var range = SelectedRange;
        _ = EditorTasks.Run(async () =>
        {
            var actions = await Workspace.CodeActions(document, range);
            PresentQuickActions(actions, range.Location);
        });
    }

    public void PresentQuickActions(IReadOnlyList<CodeAction> actions, int offset)
    {
        var items = new List<Control>();
        var fixes = actions.Where(a => a.IsFix && !a.IsFixAll).ToList();
        var fixAll = actions.Where(a => a.IsFixAll).ToList();
        var refactorings = actions.Where(a => !a.IsFix).ToList();
        void Add(CodeAction action, string symbol, Color tint)
        {
            var header = new DockPanel();
            if (action.IsFix && action.Group.Length > 2)
            {
                var badge = new Border
                {
                    Background = Palette.QuaternaryFill.Brush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 0),
                    Margin = new Thickness(16, 0, 0, 0),
                    Child = new TextBlock { Text = action.Group, FontSize = 10, Foreground = Palette.SecondaryLabel.Brush },
                };
                DockPanel.SetDock(badge, Dock.Right);
                header.Children.Add(badge);
            }

            header.Children.Add(new TextBlock { Text = action.Title, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, Icon = EditorGlyphs.Icon(symbol, tint, 13) };
            item.Click += (_, _) => _ = EditorTasks.Run(() => Workspace.Perform(action));
            items.Add(item);
        }

        void Section(string title) => items.Add(new MenuItem
        {
            Header = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.SecondaryLabel.Brush },
            IsEnabled = false,
        });

        if (actions.Count == 0)
        {
            items.Add(new MenuItem { Header = L.T("No quick actions here"), IsEnabled = false });
        }

        if (fixes.Count > 0)
        {
            Section(L.T("Fixes"));
            foreach (var fix in fixes)
            {
                var styleCop = fix.Group.StartsWith("SA", StringComparison.Ordinal);
                Add(fix, styleCop ? "checkmark.seal" : "lightbulb.fill", styleCop ? Palette.StyleCop.Current : Palette.Warning.Current);
            }
        }

        if (fixAll.Count > 0)
        {
            Section(L.T("Fix All"));
            fixAll.ForEach(a => Add(a, "wand.and.stars", Palette.StyleCop.Current));
        }

        if (refactorings.Count > 0)
        {
            Section(L.T("Refactorings"));
            refactorings.Take(40).ToList().ForEach(a => Add(a, "hammer", Palette.Accent.Current));
        }

        if (CaretRect(offset) is not { } rect)
        {
            return;
        }

        var menu = new ContextMenu
        {
            ItemsSource = items,
            PlacementTarget = TextArea.TextView,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = rect,
            PlacementAnchor = PopupAnchor.BottomLeft,
            PlacementGravity = PopupGravity.BottomRight,
            VerticalOffset = 4,
        };
        menu.Closed += (_, _) => TextArea.Focus();
        menu.Open(TextArea.TextView);
        QuickActionsMenu = menu;
    }

    /// <summary>Gets the last quick actions menu (tests read its entries).</summary>
    public ContextMenu? QuickActionsMenu { get; private set; }

    // MARK: Navigation and refactoring
    public void GoToDefinition()
    {
        if (Document is { } document)
        {
            var offset = SelectedRange.Location;
            _ = EditorTasks.Run(() => Workspace.GoToDefinition(document, offset));
        }
    }

    public void FindReferences()
    {
        if (Document is { } document)
        {
            var offset = SelectedRange.Location;
            _ = EditorTasks.Run(() => Workspace.FindReferences(document, offset));
        }
    }

    public void RenameSymbol()
    {
        if (Document is { } document)
        {
            var offset = SelectedRange.Location;
            _ = EditorTasks.Run(() => Workspace.Rename(document, offset));
        }
    }

    public void PromptGoToLine() => Workspace.Prompt = new PromptRequest(
        L.T("Go to Line"),
        L.T("Line 1 – %lld", LineCount),
        "42",
        L.T("Go"),
        text =>
        {
            if (int.TryParse(text.Trim(), out var line))
            {
                Reveal(new TextRange(OffsetOfLine(line), 0));
            }

            return Task.CompletedTask;
        });

    /// <summary>Selects a range and scrolls it into the upper third of the view, then flashes it.</summary>
    public void Reveal(TextRange range)
    {
        var length = Editor.Document.TextLength;
        var location = Math.Min(range.Location, length);
        var clamped = new TextRange(location, Math.Min(range.Length, length - location));
        SetSelectedRange(clamped);
        TextArea.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            var view = TextArea.TextView;
            var height = Editor.ViewportHeight;
            if (height > 0)
            {
                var top = view.GetVisualTopByDocumentLine(LineNumber(clamped.Location)) - (height / 3);
                var bottom = top + (height * 2 / 3) + view.DefaultLineHeight;
                var offset = Editor.VerticalOffset;
                if (top < offset)
                {
                    Editor.ScrollToVerticalOffset(Math.Max(0, top));
                }
                else if (bottom > offset + height)
                {
                    Editor.ScrollToVerticalOffset(bottom - height);
                }
            }

            TextArea.Caret.BringCaretToView();
            _lineBackgrounds.ShowFindIndicator(clamped);
        });
    }

    // MARK: Breakpoints
    public void ToggleBreakpointAtCaret() => ToggleBreakpoint(CaretLine);

    public void ToggleBreakpoint(int line)
    {
        if (Document is not { } document)
        {
            return;
        }

        document.ToggleBreakpoint(line);
        Workspace.Debugger.BreakpointsChanged(document);
        Gutter.InvalidateVisual();
        SetDiagnostics(Diagnostics);
    }

    public void EditBreakpointCondition(int line)
    {
        if (Document is not { } document)
        {
            return;
        }

        Workspace.Prompt = new PromptRequest(
            L.T("Breakpoint Condition"),
            L.T("Line %lld. The program stops only when the expression is true; leave empty to always stop.", line),
            "count > 10",
            L.T("Set"),
            condition =>
            {
                var updated = new Dictionary<int, SourceBreakpoint>(document.Breakpoints)
                {
                    [line] = new SourceBreakpoint(line, condition.Length == 0 ? null : condition),
                };
                document.Breakpoints = updated;
                Workspace.Debugger.BreakpointsChanged(document);
                Gutter.InvalidateVisual();
                return Task.CompletedTask;
            },
            document.Breakpoints.TryGetValue(line, out var existing) ? existing.Condition ?? string.Empty : string.Empty);
    }
}
