namespace VsLike.Core.Code;

/// <summary>Ergebnis der Rename-Vorprüfung: das Symbol an der Position und ob es umbenannt werden kann.</summary>
public sealed record RenameTarget(string CurrentName, string SymbolDisplay, bool CanRename, string? Reason, int ReferenceCount);
