namespace VsLike.Core.Code;

public sealed record SignatureInfo(string Prefix, IReadOnlyList<string> Parameters, string Suffix, string? Documentation)
{
    public string Label => Prefix + string.Join(", ", Parameters) + Suffix;
}
