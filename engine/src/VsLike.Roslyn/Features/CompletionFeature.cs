using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;
using RoslynCompletionList = Microsoft.CodeAnalysis.Completion.CompletionList;

namespace VsLike.Roslyn.Features;

/// <summary>Completion über Roslyns <see cref="CompletionService"/> (inkl. Typen aus nicht importierten Namespaces).</summary>
internal static class CompletionFeature
{
    private static readonly (string Tag, CompletionKind Kind)[] KindTags =
    [
        (WellKnownTags.Keyword, CompletionKind.Keyword),
        (WellKnownTags.Class, CompletionKind.Class),
        (WellKnownTags.Structure, CompletionKind.Struct),
        (WellKnownTags.Interface, CompletionKind.Interface),
        (WellKnownTags.Enum, CompletionKind.Enum),
        (WellKnownTags.EnumMember, CompletionKind.EnumMember),
        (WellKnownTags.Delegate, CompletionKind.Delegate),
        (WellKnownTags.ExtensionMethod, CompletionKind.ExtensionMethod),
        (WellKnownTags.Method, CompletionKind.Method),
        (WellKnownTags.Property, CompletionKind.Property),
        (WellKnownTags.Field, CompletionKind.Field),
        (WellKnownTags.Event, CompletionKind.Event),
        (WellKnownTags.Local, CompletionKind.Local),
        (WellKnownTags.Parameter, CompletionKind.Parameter),
        (WellKnownTags.Namespace, CompletionKind.Namespace),
        (WellKnownTags.TypeParameter, CompletionKind.TypeParameter),
        (WellKnownTags.Constant, CompletionKind.Constant),
        (WellKnownTags.Snippet, CompletionKind.Snippet),
    ];

    public static async Task<bool> ShouldTriggerAsync(Document document, int offset, char typed, CancellationToken cancellationToken)
    {
        if (CompletionService.GetService(document) is not { } service)
        {
            return false;
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return service.ShouldTriggerCompletion(text, Math.Min(offset, text.Length), CompletionTrigger.CreateInsertionTrigger(typed));
    }

    public static async Task<VsLike.Core.Code.CompletionList?> GetAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        if (CompletionService.GetService(document) is not { } service)
        {
            return null;
        }

        RoslynCompletionList list = await service.GetCompletionsAsync(document, offset, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (list.ItemsList.Count == 0)
        {
            return null;
        }

        var entries = list.ItemsList
            .Select(item => new CompletionEntry(
                item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                item.FilterText,
                item.SortText,
                KindOf(item.Tags),
                string.IsNullOrEmpty(item.InlineDescription) ? null : item.InlineDescription,
                item.Rules.MatchPriority > 0,
                new Handle(document, item)))
            .ToList();
        return new VsLike.Core.Code.CompletionList(entries, list.Span.Start, list.Span.Length);
    }

    public static async Task<string?> DescribeAsync(CompletionEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Handle is not Handle handle || CompletionService.GetService(handle.Document) is not { } service)
        {
            return null;
        }

        var description = await service.GetDescriptionAsync(handle.Document, handle.Item, cancellationToken).ConfigureAwait(false);
        return description?.Text;
    }

    public static async Task<WorkspaceEdit> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Handle is not Handle handle || CompletionService.GetService(handle.Document) is not { } service || handle.Document.FilePath is not { } path)
        {
            return WorkspaceEdit.Empty;
        }

        var change = await service.GetChangeAsync(handle.Document, handle.Item, commitCharacter: null, cancellationToken).ConfigureAwait(false);
        var edits = change.TextChanges.Select(c => new VsLike.Core.Code.TextEdit(c.Span.Start, c.Span.Length, c.NewText ?? string.Empty)).ToList();
        int? caret = change.NewPosition;
        if (caret is null)
        {
            // Ohne explizite Position steht der Cursor am Ende der letzten Änderung (nach allen Verschiebungen).
            var last = change.TextChanges.OrderBy(c => c.Span.Start).Last();
            var shift = change.TextChanges.Where(c => c.Span.Start < last.Span.Start).Sum(c => (c.NewText?.Length ?? 0) - c.Span.Length);
            caret = last.Span.Start + shift + (last.NewText?.Length ?? 0);
        }

        return new WorkspaceEdit([new FileEdit(path, edits)], path, caret);
    }

    private static CompletionKind KindOf(ImmutableArray<string> tags)
    {
        foreach (var (tag, kind) in KindTags)
        {
            if (tags.Contains(tag))
            {
                return kind;
            }
        }

        return CompletionKind.Other;
    }

    private sealed record Handle(Document Document, CompletionItem Item);
}
