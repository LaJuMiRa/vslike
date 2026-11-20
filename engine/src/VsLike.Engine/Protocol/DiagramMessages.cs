using VsLike.Diagram.Model;

namespace VsLike.Engine.Protocol;

public sealed record AddMemberParams(string Source, string TypeFullName, DiagramMember Member);

public sealed record RemoveMemberParams(string Source, string TypeFullName, string Signature);

public sealed record UpdateMemberParams(string Source, string TypeFullName, string Signature, DiagramMember Member);

public sealed record BaseListParams(string Source, string TypeFullName, string? BaseType, IReadOnlyList<string> Interfaces);

public sealed record ModifiersParams(string Source, string TypeFullName, Access Access, TypeModifiers Modifiers);

public sealed record TypeSourceParams(string Source, string TypeFullName);

public sealed record LocateParams(string Source, string TypeFullName, string? Signature);

/// <param name="Line">1-based line of the declaration (type or member), or 0 if it is not in this source.</param>
/// <param name="IdentifierOffset">Offset of the type's identifier (for rename and find references).</param>
public sealed record LocateResult(int Line, int IdentifierOffset);

public sealed record NewTypeParams(DiagramTypeKind Kind, string Name, string Folder);

public sealed record NewTypeResult(string Path, string Text, string FullName);

public sealed record ParseMemberParams(MemberKind Kind, string Text, string OwnerName);

/// <summary>The edited source; <see cref="Text"/> is null when the whole file should be deleted.</summary>
public sealed record SourceResult(string? Text);
