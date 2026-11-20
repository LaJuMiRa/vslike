using VsLike.Roslyn.Features;

namespace VsLike.Roslyn;

/// <summary>Anzahl der geladenen eingebauten Provider (für Diagnose und Tests).</summary>
public static class CodeActionProviderCounts
{
    public static int Fixes => CodeActionFeature.BuiltinFixProviderCount;

    public static int Refactorings => CodeActionFeature.BuiltinRefactoringProviderCount;
}
