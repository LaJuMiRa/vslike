namespace VsLike.Projects.Testing;

/// <summary>Zerlegt einen Testnamen <c>Ns.Sub.Class.Method(args)</c> in Namespace, Klasse und Methode.</summary>
public sealed record TestName(string FullName, string Namespace, string ClassName, string Method)
{
    public string ClassFullName => Namespace.Length == 0 ? ClassName : Namespace + "." + ClassName;

    public string MethodFullName => ClassFullName + "." + Method;

    /// <summary>Gets the argument part of a data-driven case (<c>(x: 1)</c>) or an empty string.</summary>
    public string Arguments => FullName.Length > MethodFullName.Length ? FullName[MethodFullName.Length..] : string.Empty;

    public static TestName Parse(string fullName)
    {
        var paren = fullName.IndexOf('(', StringComparison.Ordinal);
        var head = paren < 0 ? fullName : fullName[..paren];
        var parts = head.Split('.');
        return parts.Length switch
        {
            1 => new TestName(fullName, string.Empty, string.Empty, parts[0]),
            2 => new TestName(fullName, string.Empty, parts[0], parts[1]),
            _ => new TestName(fullName, string.Join('.', parts[..^2]), parts[^2], parts[^1]),
        };
    }
}
