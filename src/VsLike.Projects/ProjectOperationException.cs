namespace VsLike.Projects;

/// <summary>Ein dotnet-CLI-Aufruf ist fehlgeschlagen; <see cref="Output"/> enthält die Ausgabe für den Nutzer.</summary>
public sealed class ProjectOperationException : Exception
{
    public ProjectOperationException(string message, string output)
        : base(message)
    {
        Output = output;
    }

    public ProjectOperationException()
    {
        Output = string.Empty;
    }

    public ProjectOperationException(string message)
        : base(message)
    {
        Output = string.Empty;
    }

    public ProjectOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Output = string.Empty;
    }

    public string Output { get; }
}
