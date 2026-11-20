using VsLike.Core.Code;

namespace VsLike.App.Output;

/// <summary>Eine Zeile im Ausgabefenster; Zeilen mit Diagnose sind per Doppelklick anspringbar.</summary>
public sealed record OutputEntry(string Text, bool IsError, DiagnosticInfo? Diagnostic)
{
    public bool IsNavigable => Diagnostic?.FilePath is not null;
}
