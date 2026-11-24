using System.Globalization;
using System.Text.Json;

namespace VsLike.Kit;

public sealed record NuGetPackage(string Id, string LatestVersion, string? Description, string? Authors, long TotalDownloads, bool IsVerified, string? ProjectURL);

/// <summary>Search and version lookup against the NuGet v3 API (nuget.org by default). Sends no user data.</summary>
public sealed class NuGetClient
{
    public static readonly Uri DefaultServiceIndex = new("https://api.nuget.org/v3/index.json");

    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient _client;
    private readonly Uri _serviceIndex;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (Uri Search, Uri PackageBase)? _endpoints;

    public NuGetClient(HttpClient? client = null, Uri? serviceIndex = null)
    {
        _client = client ?? SharedClient;
        _serviceIndex = serviceIndex ?? DefaultServiceIndex;
    }

    public async Task<IReadOnlyList<NuGetPackage>> Search(string query, bool includePrerelease = false, int skip = 0, int take = 30, CancellationToken cancellationToken = default)
    {
        var endpoints = await ResolveEndpoints(cancellationToken).ConfigureAwait(false);
        var url = new UriBuilder(endpoints.Search)
        {
            Query = string.Join("&", new[]
            {
                "q=" + Uri.EscapeDataString(query),
                "skip=" + skip.ToString(CultureInfo.InvariantCulture),
                "take=" + take.ToString(CultureInfo.InvariantCulture),
                "prerelease=" + (includePrerelease ? "true" : "false"),
                "semVerLevel=2.0.0",
            }),
        }.Uri;
        using var root = await Json(url, cancellationToken).ConfigureAwait(false);
        return root.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? [.. data.EnumerateArray().Select(Package)]
            : [];
    }

    public async Task<IReadOnlyList<string>> Versions(string packageId, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var endpoints = await ResolveEndpoints(cancellationToken).ConfigureAwait(false);
        var baseText = endpoints.PackageBase.AbsoluteUri.TrimEnd('/') + "/";
        var url = new Uri(new Uri(baseText), Uri.EscapeDataString(packageId.ToLowerInvariant()) + "/index.json");
        using var root = await Json(url, cancellationToken).ConfigureAwait(false);
        if (!root.RootElement.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. versions.EnumerateArray().Select(v => v.GetString() ?? string.Empty).Where(v => includePrerelease || !v.Contains('-', StringComparison.Ordinal)).Reverse()];
    }

    internal static NuGetPackage Package(JsonElement item)
    {
        string? authors = null;
        if (item.TryGetProperty("authors", out var authorsValue))
        {
            authors = authorsValue.ValueKind switch
            {
                JsonValueKind.Array => string.Join(", ", authorsValue.EnumerateArray().Select(a => a.GetString())),
                JsonValueKind.String => authorsValue.GetString(),
                _ => null,
            };
        }

        return new NuGetPackage(
            Str(item, "id") ?? string.Empty,
            Str(item, "version") ?? string.Empty,
            Str(item, "description"),
            authors,
            item.TryGetProperty("totalDownloads", out var downloads) && downloads.ValueKind == JsonValueKind.Number ? (long)downloads.GetDouble() : 0,
            item.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            Str(item, "projectUrl"));
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<(Uri Search, Uri PackageBase)> ResolveEndpoints(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_endpoints is { } known)
            {
                return known;
            }

            using var index = await Json(_serviceIndex, cancellationToken).ConfigureAwait(false);
            var resources = index.RootElement.TryGetProperty("resources", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
            Uri? Find(string prefix)
            {
                var resource = resources.FirstOrDefault(r => Str(r, "@type")?.StartsWith(prefix, StringComparison.Ordinal) == true);
                return resource.ValueKind == JsonValueKind.Object && Str(resource, "@id") is { } id && Uri.TryCreate(id, UriKind.Absolute, out var uri) ? uri : null;
            }

            var search = Find("SearchQueryService");
            var packageBase = Find("PackageBaseAddress");
            if (search is null || packageBase is null)
            {
                throw new HttpRequestException("The NuGet service index could not be read.");
            }

            _endpoints = (search, packageBase);
            return (search, packageBase);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<JsonDocument> Json(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("VsLike-IDE");
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
