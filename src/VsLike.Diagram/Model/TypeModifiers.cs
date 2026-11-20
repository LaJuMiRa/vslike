namespace VsLike.Diagram.Model;

[Flags]
public enum TypeModifiers
{
    None = 0,
    Abstract = 1,
    Static = 2,
    Sealed = 4,
    Partial = 8,
}
