using VsLike.Core.Code;

namespace VsLike.Engine.Protocol;

public sealed record TriggerParams(string Path, int Offset, string Typed);

public sealed record HandleParams(string Path, int Handle);

public sealed record CodeActionParams(int Handle);

public sealed record RenameParams(string Path, int Offset, string NewName);

public sealed record DeclarationParams(string TypeName, string? MemberName);

public sealed record CompletionItem(
    string DisplayText,
    string FilterText,
    string SortText,
    CompletionKind Kind,
    string? InlineDescription,
    bool IsPreselected,
    int Handle);

public sealed record CompletionResult(IReadOnlyList<CompletionItem> Items, int SpanStart, int SpanLength);

public sealed record CodeAction(string Title, string Group, bool IsFix, int Handle);
