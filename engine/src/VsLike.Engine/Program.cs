using VsLike.Core.Build;
using VsLike.Engine;
using VsLike.Engine.Handlers;
using VsLike.Engine.Rpc;

// The VsLike analysis engine: Roslyn, StyleCop and the class diagram writer behind JSON-RPC on stdin/stdout.
// It has no UI; the native macOS app starts it and is its only client. Logs go to stderr.
var stdout = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

using var stop = new CancellationTokenSource();
using var state = new EngineState(new DotnetCli());
var server = new RpcServer(new RpcConnection(Console.OpenStandardInput(), stdout), Console.Error);
WorkspaceHandlers.Register(server, state);
LanguageHandlers.Register(server, state);
DiagramHandlers.Register(server, state);
server.On("shutdown", async _ =>
{
    await stop.CancelAsync().ConfigureAwait(false);
    return null;
});

await server.RunAsync(stop.Token).ConfigureAwait(false);
return 0;
