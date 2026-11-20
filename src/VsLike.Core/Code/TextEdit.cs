namespace VsLike.Core.Code;

/// <summary>Ersetzt <paramref name="Length"/> Zeichen ab <paramref name="Start"/> durch <paramref name="NewText"/>.</summary>
public sealed record TextEdit(int Start, int Length, string NewText);
