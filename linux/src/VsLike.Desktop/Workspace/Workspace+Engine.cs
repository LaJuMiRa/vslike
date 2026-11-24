using VsLike.Kit;

namespace VsLike.Desktop;

/// <summary>The analysis engine and StyleCop (part of Workspace.swift).</summary>
public sealed partial class Workspace
{
    public static string EngineDll()
    {
        if (Environment.GetEnvironmentVariable("VSLIKE_ENGINE_DLL") is { Length: > 0 } overridePath)
        {
            return overridePath;
        }

        var installed = Path.Combine(AppPaths.InstallFolder, "engine", "VsLikeEngine.dll");
        if (File.Exists(installed) || AppPaths.RepositoryRoot is not { } repository)
        {
            return installed;
        }

        // Development: the engine built from this repository.
        return Path.Combine(repository, "engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll");
    }

    public async Task RestartEngine()
    {
        if (SolutionPath is null)
        {
            return;
        }

        if (Engine is { } engine)
        {
            Engine = null;
            await engine.Stop();
            engine.Dispose();
        }

        await StartEngine(SolutionPath);
    }

    /// <summary>Pushes a document's text to the engine now (before requests that need the latest text).</summary>
    public void SyncNow(CodeDocument document)
    {
        if (_syncTasks.Remove(document.Id, out var pending))
        {
            pending.Cancel();
        }

        if (document.IsCSharp && IsEngineReady)
        {
            Engine?.UpdateDocument(document.Path, document.Text);
        }
    }

    public Task ToggleStyleCop() => ApplyStyleCop(!StyleCopEnabled, persist: true);

    /// <summary>Opens the solution's stylecop.json, creating it first with the company name and copyright text from the settings.</summary>
    public async Task OpenStyleCopSettings()
    {
        if (SolutionPath is null)
        {
            return;
        }

        var path = StyleCopSettingsFile.Path(SolutionPath);
        if (!File.Exists(path))
        {
            try
            {
                await File.WriteAllTextAsync(path, StyleCopSettingsFile.Contents(Preferences.StyleCopCompanyName, Preferences.StyleCopCopyrightText));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Alert = new AlertMessage("StyleCop", ex.Message);
                return;
            }

            RefreshTree();
            await ReloadStyleCopSettings();
        }

        OpenFile(path);
    }

    /// <summary>Makes the engine read stylecop.json again (after it was created or saved).</summary>
    public async Task ReloadStyleCopSettings()
    {
        if (!StyleCopEnabled || Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        try
        {
            await engine.SetStyleCop(true);
            Status = new StatusMessage(L.T("StyleCop settings loaded"), StatusStyle.Success);
        }
        catch (EngineException ex)
        {
            Alert = new AlertMessage("StyleCop", ex.Message);
        }
    }

    public async Task ApplyStyleCop(bool enabled, bool persist)
    {
        StyleCopEnabled = enabled;
        if (persist && SolutionPath is not null)
        {
            Settings = Settings with { StyleCopEnabled = enabled };
            TrySaveSettings();
        }

        if (Engine is not { } engine || !IsEngineReady)
        {
            return;
        }

        IsStyleCopLoading = true;
        try
        {
            Status = new StatusMessage(enabled ? L.T("Loading StyleCop…") : L.T("StyleCop off"), enabled ? StatusStyle.Progress : StatusStyle.Neutral);
            var loaded = await engine.SetStyleCop(enabled);
            if (enabled && !loaded)
            {
                StyleCopEnabled = false;
                Alert = new AlertMessage("StyleCop", L.T("StyleCop could not be downloaded. Check your internet connection and try again."));
                return;
            }

            Status = new StatusMessage(enabled ? L.T("StyleCop on") : L.T("StyleCop off"), enabled ? StatusStyle.Success : StatusStyle.Neutral);
        }
        catch (EngineException ex)
        {
            Alert = new AlertMessage("StyleCop", ex.Message);
        }
        finally
        {
            IsStyleCopLoading = false;
        }
    }

    internal void TrySaveSettings()
    {
        if (SolutionPath is null)
        {
            return;
        }

        try
        {
            Settings.Save(SolutionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience (try? in the original).
        }
    }

    private async Task StartEngine(string solutionPath)
    {
        EngineStatus = EngineStatus.Starting;
        Status = new StatusMessage(L.T("Starting analysis…"), StatusStyle.Progress);
        EngineClient? engine = null;
        try
        {
            var runner = CommandRunner.Dotnet(Preferences.EffectiveLanguage);
            engine = new EngineClient(EngineDll(), runner);
            var client = engine;
            engine.Start(code => Post(() => EngineExited(code, client)));
            Engine = engine;
            Listen(engine);
            var info = await engine.Initialize(Preferences.EffectiveLanguage);
            Output.Append($"Analysis engine {info.EngineVersion}, .NET SDK {info.SdkVersion}", OutputLog.Channel.Engine);
            EngineStatus = EngineStatus.Loading;
            Status = new StatusMessage(L.T("Loading %@…", Path.GetFileName(solutionPath)), StatusStyle.Progress);
            var result = await engine.Load(solutionPath);
            Projects = result.Projects;
            foreach (var warning in result.Warnings)
            {
                Output.Append(warning, OutputLog.Channel.Engine, isError: true);
            }

            foreach (var document in OpenDocuments.Where(d => d.IsCSharp))
            {
                engine.UpdateDocument(document.Path, document.Text);
            }

            EngineStatus = EngineStatus.Ready;
            Status = new StatusMessage(L.T("%lld projects loaded", result.Projects.Count), StatusStyle.Success);
            if (StyleCopEnabled)
            {
                await ApplyStyleCop(true, persist: false);
            }

            await Run(engine.RefreshAllDiagnostics);
            NotifyCodeChanged();
        }
        catch (Exception ex) when (ex is EngineException or CommandException or IOException or System.ComponentModel.Win32Exception)
        {
            EngineStatus = EngineStatus.Failed(ex.Message);
            Status = new StatusMessage(L.T("Analysis unavailable"), StatusStyle.Error);
            Output.Append(ex.Message, OutputLog.Channel.Engine, isError: true);
            if (engine?.RecentLog is { Length: > 0 } log)
            {
                Output.Append(log, OutputLog.Channel.Engine, isError: true);
            }
        }
    }

    private void EngineExited(int code, EngineClient client)
    {
        if (!ReferenceEquals(client, Engine) || EngineStatus == EngineStatus.Idle)
        {
            return;
        }

        EngineStatus = EngineStatus.Failed(L.T("The analysis engine stopped (exit %d).", code));
        Status = new StatusMessage(L.T("Analysis stopped"), StatusStyle.Error);
        Output.Append(client.RecentLog, OutputLog.Channel.Engine, isError: true);
    }

    private void Listen(EngineClient engine)
    {
        _notificationTask?.Cancel();
        var cancellation = new CancellationTokenSource();
        _notificationTask = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var notification in engine.Notifications.ReadAllAsync(cancellation.Token))
                {
                    if (notification.Method != "workspace/diagnostics")
                    {
                        continue;
                    }

                    var list = DecodeDiagnostics(notification);
                    var path = notification.Params.StringValue("path");
                    if (list is not null)
                    {
                        Post(() =>
                        {
                            if (!cancellation.IsCancellationRequested)
                            {
                                Diagnostics.SetLive(list, path);
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Closed.
            }
        });
    }

    private static IReadOnlyList<Diagnostic>? DecodeDiagnostics(EngineNotification notification)
    {
        try
        {
            return JSONValue.Decode<IReadOnlyList<Diagnostic>>(notification.Params.Child("diagnostics"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void DocumentChanged(CodeDocument document)
    {
        if (_syncTasks.Remove(document.Id, out var previous))
        {
            previous.Cancel();
        }

        var cancellation = new CancellationTokenSource();
        _syncTasks[document.Id] = cancellation;
        _ = Debounced(TimeSpan.FromMilliseconds(120), cancellation.Token, () =>
        {
            _syncTasks.Remove(document.Id);
            SyncNow(document);
            NotifyCodeChanged();
        });
        foreach (var listener in DocumentListeners)
        {
            listener(document);
        }
    }
}
