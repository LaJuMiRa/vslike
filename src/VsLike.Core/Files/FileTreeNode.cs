namespace VsLike.Core.Files;

public sealed record FileTreeNode(string Name, string FullPath, bool IsDirectory, IReadOnlyList<FileTreeNode> Children);
