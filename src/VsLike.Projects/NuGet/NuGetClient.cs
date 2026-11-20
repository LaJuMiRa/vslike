using System.Net.Http.Json;
using System.Text.Json;

namespace VsLike.Projects.NuGet;

/// <summary>
/// Schlanker Client für die NuGet-v3-API (Suche und Versionsliste). Installation selbst läuft über
/// <c>dotnet add package</c>, damit Paketquellen und Authentifizierung des SDK gelten.
/// </summary>
public sealed class NuGetClient
{
    public const string DefaultServiceIndex = "https://api.nuget.org/v3/index.json";

    private readonly HttpClient _http;
    private readonly string _serviceIndex;
    private (string Search, string PackageBase)? _endpoints;

    public NuGetClient(HttpClient http, string serviceIndex = DefaultServiceIndex)
    {
        _http = http;
        _serviceIndex = serviceIndex;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("VsLike-IDE");
        }
    }

    public async Task<IReadOnlyList<NuGetPackage>> SearchAsync(string query, bool includePrerelease, int skip, int take, CancellationToken cancellationToken)
    {
        var (search, _) = await EndpointsAsync(cancellationToken).ConfigureAwait(false);
        var url = $"{search}?q={Uri.EscapeDataString(query)}&skip={skip}&take={take}&prerelease={(includePrerelease ? "true" : "false")}&semVerLevel=2.0.0";
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("data").EnumerateArray().Select(p => new NuGetPackage(
            p.GetProperty("id").GetString() ?? string.Empty,
            p.GetProperty("version").GetString() ?? string.Empty,
            Text(p, "description"),
            p.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array ? string.Join(", ", authors.EnumerateArray().Select(a => a.GetString())) : Text(p, "authors"),
            p.TryGetProperty("totalDownloads", out var downloads) && downloads.TryGetInt64(out var count) ? count : 0,
            p.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            Text(p, "projectUrl"))).ToList();
    }

    /// <summary>Returns all versions, newest first.</summary>
    public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease, CancellationToken cancellationToken)
    {
        var (_, packageBase) = await EndpointsAsync(cancellationToken).ConfigureAwait(false);
        using var document = await GetJsonAsync($"{packageBase.TrimEnd('/')}/{packageId.ToLowerInvariant()}/index.json", cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => v.GetString() ?? string.Empty)
            .Where(v => includePrerelease || !v.Contains('-', StringComparison.Ordinal))
            .Reverse()
            .ToList();
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<(string Search, string PackageBase)> EndpointsAsync(CancellationToken cancellationToken)
    {
        if (_endpoints is { } cached)
        {
            return cached;
        }

        using var index = await GetJsonAsync(_serviceIndex, cancellationToken).ConfigureAwait(false);
        var resources = index.RootElement.GetProperty("resources").EnumerateArray()
            .Select(r => (Type: r.GetProperty("@type").GetString() ?? string.Empty, Id: r.GetProperty("@id").GetString() ?? string.Empty))
            .ToList();
        var search = resources.FirstOrDefault(r => r.Type.StartsWith("SearchQueryService", StringComparison.Ordinal)).Id;
        var packageBase = resources.FirstOrDefault(r => r.Type.StartsWith("PackageBaseAddress", StringComparison.Ordinal)).Id;
        if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(packageBase))
        {
            throw new InvalidOperationException($"NuGet service index {_serviceIndex} lacks search or package base resources.");
        }

        _endpoints = (search, packageBase);
        return (search, packageBase);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
