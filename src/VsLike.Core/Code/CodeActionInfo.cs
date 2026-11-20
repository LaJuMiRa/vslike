namespace VsLike.Core.Code;

/// <summary>Ein Code-Fix oder Refactoring (⌘.). <see cref="Handle"/> ist ein undurchsichtiger Verweis der Implementierung.</summary>
/// <param name="Group">Display group, e.g. "Fix" or "Refactor"; nested actions use the parent title as group.</param>
public sealed record CodeActionInfo(string Title, string Group, bool IsFix, object Handle);
