using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Code;

namespace VsLike.Roslyn.Features;

/// <summary>Go to Definition, Find References und Rename über die öffentlichen Roslyn-APIs.</summary>
internal static class NavigationFeature
{
    public static async Task<ISymbol?> FindSymbolAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null && offset > 0)
        {
            symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset - 1, cancellationToken).ConfigureAwait(false);
        }

        return symbol switch
        {
            null => null,
            IMethodSymbol { ReducedFrom: { } reduced } => reduced,
            _ => symbol.OriginalDefinition,
        };
    }

    public static async Task<IReadOnlyList<SourceLocation>> GoToDefinitionAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var symbol = await FindSymbolAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        var locations = new List<SourceLocation>();
        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            locations.Add(await ToSourceLocationAsync(location, cancellationToken).ConfigureAwait(false));
        }

        return locations;
    }

    public static async Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var symbol = await FindSymbolAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        var references = await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(false);
        var locations = new List<SourceLocation>();
        foreach (var location in references.SelectMany(r => r.Definition.Locations.Where(l => l.IsInSource).Concat(r.Locations.Select(l => l.Location))))
        {
            locations.Add(await ToSourceLocationAsync(location, cancellationToken).ConfigureAwait(false));
        }

        return [.. locations.DistinctBy(l => (l.FilePath, l.Start)).OrderBy(l => l.FilePath, StringComparer.Ordinal).ThenBy(l => l.Start)];
    }

    public static async Task<RenameTarget?> PrepareRenameAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var symbol = await FindSymbolAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        var inSource = symbol.Locations.Any(l => l.IsInSource);
        var references = inSource ? await FindReferencesAsync(document, offset, cancellationToken).ConfigureAwait(false) : [];
        return new RenameTarget(
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            inSource && !symbol.IsImplicitlyDeclared,
            inSource ? null : "Symbol is defined in metadata",
            references.Count);
    }

    public static async Task<WorkspaceEdit> RenameAsync(Document document, int offset, string newName, CancellationToken cancellationToken)
    {
        var symbol = await FindSymbolAsync(document, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null || !symbol.Locations.Any(l => l.IsInSource))
        {
            return WorkspaceEdit.Empty;
        }

        var solution = document.Project.Solution;
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(RenameOverloads: true), newName, cancellationToken).ConfigureAwait(false);
        return await SolutionDiff.CreateAsync(solution, renamed, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> EnclosingTypeNameAsync(Document document, int offset, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root?.FindToken(Math.Max(0, offset - 1)).Parent?.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
    }

    /// <summary>Finds a type by metadata name (<c>Ns.Outer+Inner</c> or dotted) and optionally one of its members in source.</summary>
    public static async Task<SourceLocation?> FindDeclarationAsync(Solution solution, string typeName, string? memberName, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            {
                continue;
            }

            var type = compilation.GetTypeByMetadataName(typeName) ?? compilation.GetTypeByMetadataName(NestedName(typeName));
            if (type is null || !type.Locations.Any(l => l.IsInSource))
            {
                continue;
            }

            var symbol = memberName is null ? type : type.GetMembers(memberName).FirstOrDefault(m => m.Locations.Any(l => l.IsInSource)) ?? (ISymbol)type;
            return await ToSourceLocationAsync(symbol.Locations.First(l => l.IsInSource), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static string NestedName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[..dot] + "+" + typeName[(dot + 1)..];
    }

    private static async Task<SourceLocation> ToSourceLocationAsync(Location location, CancellationToken cancellationToken)
    {
        var span = location.GetLineSpan();
        var text = location.SourceTree is { } tree ? await tree.GetTextAsync(cancellationToken).ConfigureAwait(false) : null;
        var lineText = text is null ? string.Empty : text.Lines[span.StartLinePosition.Line].ToString().Trim();
        return new SourceLocation(span.Path, location.SourceSpan.Start, location.SourceSpan.Length, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, lineText);
    }
}
