using Microsoft.CodeAnalysis;
using VsLike.Core.Projects;
using VsLike.Diagram.Code;
using VsLike.Diagram.Model;
using VsLike.Engine.Protocol;
using VsLike.Engine.Rpc;

namespace VsLike.Engine.Handlers;

/// <summary>
/// The code side of the class diagram: reading the model from the solution and rewriting single declarations. The app
/// owns files, undo and layout; every writer call gets a source text and returns the changed text.
/// </summary>
public static class DiagramHandlers
{
    public static void Register(RpcServer server, EngineState state)
    {
        var workspace = state.Workspace;
        server.On("diagram/model", async ct =>
            workspace.CurrentSolution is { } solution ? await DiagramReader.ReadAsync(solution, ct).ConfigureAwait(false) : DiagramModel.Empty);
        server.On<AddMemberParams>("diagram/addMember", (p, _) => Text(DiagramWriter.AddMember(p.Source, p.TypeFullName, p.Member)));
        server.On<RemoveMemberParams>("diagram/removeMember", (p, _) => Text(DiagramWriter.RemoveMember(p.Source, p.TypeFullName, p.Signature)));
        server.On<UpdateMemberParams>("diagram/updateMember", (p, _) => Text(DiagramWriter.UpdateMember(p.Source, p.TypeFullName, p.Signature, p.Member)));
        server.On<BaseListParams>("diagram/setBaseList", (p, _) => Text(DiagramWriter.SetBaseList(p.Source, p.TypeFullName, p.BaseType, p.Interfaces)));
        server.On<ModifiersParams>("diagram/setModifiers", (p, _) => Text(DiagramWriter.SetTypeModifiers(p.Source, p.TypeFullName, p.Access, p.Modifiers)));
        server.On<TypeSourceParams>("diagram/removeType", (p, _) => Text(DiagramWriter.RemoveType(p.Source, p.TypeFullName)));
        server.On<LocateParams>("diagram/locate", (p, _) => Task.FromResult<object?>(Locate(p)));
        server.On<ParseMemberParams>("diagram/parseMember", (p, _) =>
            Task.FromResult<object?>(MemberSpec.TryParse(p.Kind, p.Text, p.OwnerName, out var member) ? member : null));
        server.On<NewTypeParams>("diagram/newType", (p, _) => Task.FromResult<object?>(NewType(state, p)));
    }

    private static Task<object?> Text(string? text) => Task.FromResult<object?>(new SourceResult(text));

    private static LocateResult Locate(LocateParams p)
    {
        var root = SyntaxLocator.Parse(p.Source);
        var declaration = SyntaxLocator.Types(root).FirstOrDefault(t => SyntaxLocator.FullNameOf(t) == p.TypeFullName);
        if (declaration is null)
        {
            return new LocateResult(0, -1);
        }

        SyntaxNode node = declaration;
        if (p.Signature is not null)
        {
            try
            {
                node = SyntaxLocator.FindMember(declaration, p.Signature);
            }
            catch (InvalidOperationException)
            {
                return new LocateResult(0, declaration.Identifier.SpanStart);
            }
        }

        return new LocateResult(node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, declaration.Identifier.SpanStart);
    }

    /// <summary>Source of <c>&lt;Name&gt;.cs</c> in a folder, with the namespace from RootNamespace plus the folder path.</summary>
    private static NewTypeResult NewType(EngineState state, NewTypeParams p)
    {
        var identifier = NamespaceResolver.ToIdentifier(p.Name.Trim());
        if (identifier.Length == 0 || identifier.Contains('.', StringComparison.Ordinal))
        {
            throw new RpcException(RpcException.InvalidParams, $"'{p.Name}' is not a valid type name.");
        }

        var folder = Path.GetFullPath(p.Folder);
        var project = state.Workspace.Projects
            .Where(x => IsInside(folder, Path.GetDirectoryName(x.FilePath)!))
            .MaxBy(x => x.FilePath.Length);
        var ns = project is null ? null : NamespaceResolver.Resolve(project.FilePath, project.RootNamespace, folder);
        var fullName = string.IsNullOrEmpty(ns) ? identifier : ns + "." + identifier;
        var type = new DiagramType(fullName, identifier, ns ?? string.Empty, p.Kind);
        return new NewTypeResult(Path.Combine(folder, identifier + ".cs"), CodeText.TypeFile(type, ns), fullName);
    }

    private static bool IsInside(string folder, string directory)
        => folder == directory || folder.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
