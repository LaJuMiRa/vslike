using VsLike.Core.Localization;
using VsLike.Projects.Cli;

namespace VsLike.App.Services;

/// <summary>Prüft beim Start, ob ein .NET SDK ab Version 10 installiert ist, und erklärt sonst die Installation.</summary>
public static class SdkCheck
{
    public static async Task<bool> EnsureSdkAsync(Workbench workbench)
    {
        var sdks = await DotnetSdks.ListAsync(workbench.Cli, CancellationToken.None).ConfigureAwait(true);
        if (DotnetSdks.HasSupportedSdk(sdks))
        {
            return true;
        }

        await workbench.Dialogs.ShowMessageAsync(Loc.Get("Sdk.Missing.Title"), Loc.Get("Sdk.Missing.Body")).ConfigureAwait(true);
        return false;
    }
}
