namespace VsLike.Core.Code;

/// <summary>Parameterhilfe für einen Aufruf: alle Überladungen und der aktuelle Parameter.</summary>
public sealed record SignatureHelp(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter);
