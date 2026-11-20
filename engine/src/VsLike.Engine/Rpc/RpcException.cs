namespace VsLike.Engine.Rpc;

/// <summary>An error that is sent back to the client as a JSON-RPC error object.</summary>
public sealed class RpcException : Exception
{
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int RequestCancelled = -32800;

    /// <summary>A handled failure the app shows to the user (e.g. "type not found").</summary>
    public const int OperationFailed = 1;

    public RpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public RpcException()
    {
    }

    public RpcException(string message)
        : base(message)
    {
    }

    public RpcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Code { get; } = InternalError;
}
