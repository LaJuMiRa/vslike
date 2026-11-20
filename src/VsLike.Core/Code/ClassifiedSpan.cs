namespace VsLike.Core.Code;

/// <param name="Classification">Semantic classification, see <see cref="SemanticClassifications"/>.</param>
public sealed record ClassifiedSpan(int Start, int Length, string Classification);
