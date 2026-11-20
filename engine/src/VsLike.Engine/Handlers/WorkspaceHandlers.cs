using System.Reflection;
using VsLike.Core.Localization;
using VsLike.Engine.Protocol;
using VsLike.Engine.Rpc;
using VsLike.Roslyn;

namespace VsLike.Engine.Handlers;

/// <summary>Loading solutions, syncing editor text, diagnostics, classification, hover and StyleCop.</summary>
public static class WorkspaceHandlers
{
    public static void Register(RpcServer server, EngineState state)
    {
        var workspace = state.Workspace;
        workspace.DiagnosticsChanged += (_, e) => _ = server.Connection.NotifyAsync("workspace/diagnostics", new DiagnosticsNotification(e.FilePath, e.Diagnostics), CancellationToken.None);

        server.On<InitializeParams>("initialize", (p, _) =>
        {
            EngineText.SetCulture(p.Culture);
            var instance = MSBuildBootstrap.EnsureRegistered();
            var version = typeof(WorkspaceHandlers).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
            return Result(new InitializeResult(version, instance.MSBuildPath, instance.Version.ToString()));
        });
        server.On<PathParams>("workspace/load", async (p, ct) => await workspace.LoadAsync(p.Path, ct).ConfigureAwait(false));
        server.On("workspace/projects", _ => Result(workspace.Projects));
        server.On<TextParams>("document/update", (p, _) =>
        {
            workspace.UpdateDocumentText(p.Path, p.Text);
            return Result(null);
        });
        server.On<TextParams>("document/add", (p, _) => Result(workspace.AddDocument(p.Path, p.Text)));
        server.On<PathParams>("document/remove", (p, _) => Result(workspace.RemoveDocument(p.Path)));
        server.On<PathParams>("document/diagnostics", async (p, ct) => await workspace.GetDocumentDiagnosticsAsync(p.Path, ct).ConfigureAwait(false));
        server.On("workspace/refreshDiagnostics", async ct =>
        {
            await workspace.RefreshAllDiagnosticsAsync(ct).ConfigureAwait(false);
            return null;
        });
        server.On<RangeParams>("document/classify", async (p, ct) => await workspace.ClassifyAsync(p.Path, p.Start, p.Length, ct).ConfigureAwait(false));
        server.On<OffsetParams>("document/quickInfo", async (p, ct) => await workspace.GetQuickInfoAsync(p.Path, p.Offset, ct).ConfigureAwait(false));
        server.On<StyleCopParams>("styleCop/set", async (p, ct) => await workspace.SetStyleCopEnabledAsync(p.Enabled, state.Cli, ct).ConfigureAwait(false));
    }

    private static Task<object?> Result(object? value) => Task.FromResult(value);
}
