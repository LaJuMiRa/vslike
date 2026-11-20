namespace VsLike.Debugging.Dap;

public sealed class DapException : Exception
{
    public DapException()
    {
    }

    public DapException(string message)
        : base(message)
    {
    }

    public DapException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
