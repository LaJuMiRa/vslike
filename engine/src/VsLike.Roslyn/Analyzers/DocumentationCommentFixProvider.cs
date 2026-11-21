using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using VsLike.Core.Localization;

namespace VsLike.Roslyn.Analyzers;

/// <summary>
/// "Add documentation comment" for SA1600/SA1601/SA1602 (and CS1591): inserts a <c>///</c> comment with summary,
/// type parameters, parameters and return value above the element, or <c>&lt;inheritdoc/&gt;</c> for overrides and
/// interface implementations. Replaces StyleCop's own SA1600 fix, which only offers "inherit documentation".
/// </summary>
internal sealed class DocumentationCommentFixProvider : CodeFixProvider
{
    public const string EquivalenceKey = "VsLike.AddDocumentationComment";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["SA1600", "SA1601", "SA1602", "CS1591"];

    public override FixAllProvider GetFixAllProvider() => FixAll.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (FindMember(root, diagnostic.Location.SourceSpan) is null)
            {
                continue;
            }

            var document = context.Document;
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(
                CodeAction.Create(EngineText.Format("CodeAction.AddDocumentation"), ct => AddAsync(document, [span], ct), EquivalenceKey),
                diagnostic);
        }
    }

    private static MemberDeclarationSyntax? FindMember(SyntaxNode root, TextSpan span)
        => root.FindToken(span.Start).Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();

    /// <summary>Documents the elements at all spans in one pass (offsets of the original text).</summary>
    private static async Task<Document> AddAsync(Document document, IEnumerable<TextSpan> spans, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newline = text.Lines.Count > 1 && text.ToString(TextSpan.FromBounds(text.Lines[0].End, text.Lines[0].EndIncludingLineBreak)) == "\r\n" ? "\r\n" : "\n";
        var changes = spans
            .Select(span => FindMember(root, span))
            .OfType<MemberDeclarationSyntax>()
            .Where(member => !HasDocumentation(member))
            .Distinct()
            .Select(member => Comment(member, model, text, newline, cancellationToken))
            .OrderBy(change => change.Span.Start)
            .ToList();
        return changes.Count == 0 ? document : document.WithText(text.WithChanges(changes));
    }

    private static TextChange Comment(MemberDeclarationSyntax member, SemanticModel? model, SourceText text, string newline, CancellationToken cancellationToken)
    {
        var symbol = model?.GetDeclaredSymbol(member is BaseFieldDeclarationSyntax field ? field.Declaration.Variables.First() : member, cancellationToken);
        var lines = DocumentationText.For(member, symbol);

        // Insert at the start of the element's first line (before attributes), with the element's indentation.
        var line = text.Lines.GetLineFromPosition(member.SpanStart);
        var before = text.ToString(TextSpan.FromBounds(line.Start, member.SpanStart));
        var indentation = new string([.. before.TakeWhile(char.IsWhiteSpace)]);
        if (before.Length != indentation.Length)
        {
            // Another element on the same line (enum values in one line): break the line first.
            return new TextChange(new TextSpan(member.SpanStart, 0), newline + string.Concat(lines.Select(l => $"{indentation}    /// {l}{newline}")) + indentation + "    ");
        }

        return new TextChange(new TextSpan(line.Start, 0), string.Concat(lines.Select(l => $"{indentation}/// {l}{newline}")));
    }

    private static bool HasDocumentation(SyntaxNode member)
        => member.GetLeadingTrivia().Any(t => t.GetStructure() is DocumentationCommentTriviaSyntax);

    /// <summary>Fix all in document or project: every SA1600/SA1601/SA1602 element of a document in one pass.</summary>
    private sealed class FixAll : FixAllProvider
    {
        public static readonly FixAll Instance = new();

        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes() => [FixAllScope.Document, FixAllScope.Project];

        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var title = EngineText.Format("CodeAction.AddDocumentation");
            return Task.FromResult<CodeAction?>(CodeAction.Create(title, ct => FixAsync(fixAllContext, ct), EquivalenceKey));
        }

        private static async Task<Solution> FixAsync(FixAllContext context, CancellationToken cancellationToken)
        {
            var documents = context.Scope == FixAllScope.Document && context.Document is { } single ? [single] : context.Project.Documents.ToList();
            var solution = context.Solution;
            foreach (var document in documents)
            {
                var diagnostics = await context.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
                var spans = diagnostics.Where(d => context.DiagnosticIds.Contains(d.Id)).Select(d => d.Location.SourceSpan).ToList();
                if (spans.Count > 0)
                {
                    var fixedDocument = await AddAsync(document, spans, cancellationToken).ConfigureAwait(false);
                    solution = solution.WithDocumentText(document.Id, await fixedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false));
                }
            }

            return solution;
        }
    }
}
