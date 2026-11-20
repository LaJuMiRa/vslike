using VsLike.Core.Code;
using VsLike.Engine.Protocol;
using VsLike.Engine.Rpc;

namespace VsLike.Engine.Handlers;

/// <summary>IntelliSense, code actions, formatting and navigation.</summary>
public static class LanguageHandlers
{
    public static void Register(RpcServer server, EngineState state)
    {
        var features = state.Features;
        server.On<TriggerParams>("completion/shouldTrigger", async (p, ct) =>
            p.Typed.Length == 1 && await features.ShouldTriggerCompletionAsync(p.Path, p.Offset, p.Typed[0], ct).ConfigureAwait(false));
        server.On<OffsetParams>("completion/list", async (p, ct) =>
        {
            var list = await features.GetCompletionAsync(p.Path, p.Offset, ct).ConfigureAwait(false);
            state.Completions.Clear();
            if (list is null)
            {
                return null;
            }

            var items = new List<CompletionItem>(list.Items.Count);
            foreach (var entry in list.Items)
            {
                var handle = state.NextHandle();
                state.Completions[handle] = entry;
                items.Add(new CompletionItem(entry.DisplayText, entry.FilterText, entry.SortText, entry.Kind, entry.InlineDescription, entry.IsPreselected, handle));
            }

            return new CompletionResult(items, list.SpanStart, list.SpanLength);
        });
        server.On<HandleParams>("completion/description", async (p, ct) =>
            state.Completions.TryGetValue(p.Handle, out var entry) ? await features.GetCompletionDescriptionAsync(p.Path, entry, ct).ConfigureAwait(false) : null);
        server.On<HandleParams>("completion/resolve", async (p, ct) =>
            state.Completions.TryGetValue(p.Handle, out var entry) ? await features.ResolveCompletionAsync(p.Path, entry, ct).ConfigureAwait(false) : WorkspaceEdit.Empty);
        server.On<OffsetParams>("signatureHelp", async (p, ct) => await features.GetSignatureHelpAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<RangeParams>("codeActions/list", async (p, ct) =>
        {
            var actions = await features.GetCodeActionsAsync(p.Path, p.Start, p.Length, ct).ConfigureAwait(false);
            state.CodeActions.Clear();
            var result = new List<CodeAction>(actions.Count);
            foreach (var action in actions)
            {
                var handle = state.NextHandle();
                state.CodeActions[handle] = action;
                result.Add(new CodeAction(action.Title, action.Group, action.IsFix, handle));
            }

            return result;
        });
        server.On<CodeActionParams>("codeActions/resolve", async (p, ct) =>
            state.CodeActions.TryGetValue(p.Handle, out var action) ? await features.GetCodeActionEditAsync(action, ct).ConfigureAwait(false) : WorkspaceEdit.Empty);
        server.On<PathParams>("document/format", async (p, ct) => await features.FormatDocumentAsync(p.Path, ct).ConfigureAwait(false));
        server.On<OffsetParams>("nav/definition", async (p, ct) => await features.GoToDefinitionAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<OffsetParams>("nav/references", async (p, ct) => await features.FindReferencesAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<DeclarationParams>("nav/declaration", async (p, ct) => await features.FindDeclarationAsync(p.TypeName, p.MemberName, ct).ConfigureAwait(false));
        server.On<OffsetParams>("document/enclosingType", async (p, ct) => await features.GetEnclosingTypeNameAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<OffsetParams>("rename/prepare", async (p, ct) => await features.PrepareRenameAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<RenameParams>("rename/apply", async (p, ct) => await features.RenameAsync(p.Path, p.Offset, p.NewName, ct).ConfigureAwait(false));
    }
}
