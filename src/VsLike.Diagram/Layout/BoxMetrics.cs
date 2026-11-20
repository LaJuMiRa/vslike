using VsLike.Diagram.Model;

namespace VsLike.Diagram.Layout;

/// <summary>
/// Maße einer Typ-Box in Diagrammkoordinaten. Mitgliederzeilen stehen in Monospace, daher hängen die Maße nur von der
/// Zeichenzahl ab und sind in Canvas, Trefferprüfung und SVG-Export identisch.
/// </summary>
public static class BoxMetrics
{
    public const double FontSize = 12;
    public const double CharWidth = FontSize * 0.6;
    public const double LineHeight = 18;
    public const double HeaderHeight = 40;
    public const double Padding = 10;
    public const double MinWidth = 140;
    public const double GridSize = 20;

    public static string Stereotype(DiagramType type) => type.Kind switch
    {
        DiagramTypeKind.Interface => "«interface»",
        DiagramTypeKind.Enum => "«enum»",
        DiagramTypeKind.Struct => "«struct»",
        DiagramTypeKind.Record => "«record»",
        DiagramTypeKind.RecordStruct => "«record struct»",
        _ when type.Modifiers.HasFlag(TypeModifiers.Static) => "«static»",
        _ when type.Modifiers.HasFlag(TypeModifiers.Abstract) => "«abstract»",
        _ => type.IsExternal ? "«extern»" : string.Empty,
    };

    public static string Symbol(Access access) => access switch
    {
        Access.Public => "+",
        Access.Protected or Access.ProtectedInternal or Access.PrivateProtected => "#",
        Access.Internal => "~",
        _ => "-",
    };

    /// <summary>One UML-style line per member, e.g. <c>+ Total(withTax: bool): decimal</c>.</summary>
    public static string Line(DiagramMember member) => member.Kind switch
    {
        MemberKind.EnumValue => member.Value is null ? member.Name : $"{member.Name} = {member.Value}",
        MemberKind.Method => $"{Symbol(member.Access)} {member.Name}({Parameters(member)}): {member.Type}",
        MemberKind.Constructor => $"{Symbol(member.Access)} {member.Name}({Parameters(member)})",
        MemberKind.Event => $"{Symbol(member.Access)} «event» {member.Name}: {member.Type}",
        _ => $"{Symbol(member.Access)} {member.Name}: {member.Type}",
    };

    public static IReadOnlyList<string> Lines(DiagramType type, bool collapsed)
        => collapsed || type.IsExternal ? [] : type.MemberList.Select(Line).ToList();

    public static (double Width, double Height) Size(DiagramType type, bool collapsed)
    {
        var lines = Lines(type, collapsed);
        var longest = lines.Select(l => l.Length).DefaultIfEmpty(0).Max();
        var header = Math.Max(type.DisplayName.Length * CharWidth * 1.15, Stereotype(type).Length * CharWidth);
        var width = Math.Max(MinWidth, Math.Max(header, longest * CharWidth) + (2 * Padding));
        var height = HeaderHeight + (lines.Count == 0 ? 0 : (lines.Count * LineHeight) + Padding);
        return (Snap(width, up: true), height);
    }

    public static double Snap(double value, bool up = false)
        => up ? Math.Ceiling(value / GridSize) * GridSize : Math.Round(value / GridSize) * GridSize;

    private static string Parameters(DiagramMember member) => string.Join(", ", member.ParameterList.Select(p => $"{p.Name}: {p.Type}"));
}
