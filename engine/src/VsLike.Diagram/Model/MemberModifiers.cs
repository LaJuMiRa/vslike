namespace VsLike.Diagram.Model;

[Flags]
public enum MemberModifiers
{
    None = 0,
    Static = 1,
    Abstract = 2,
    Virtual = 4,
    Override = 8,
    ReadOnly = 16,
    Const = 32,
    Async = 64,
    Required = 128,
}
