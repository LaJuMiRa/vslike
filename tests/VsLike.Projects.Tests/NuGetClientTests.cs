using System.Net;
using VsLike.Projects.NuGet;

namespace VsLike.Projects.Tests;

public class NuGetClientTests
{
    [Fact]
    public async Task SearchAndVersions_UseServiceIndexEndpoints()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["https://example.test/index.json"] = """{"resources":[{"@id":"https://example.test/search","@type":"SearchQueryService/3.5.0"},{"@id":"https://example.test/flat/","@type":"PackageBaseAddress/3.0.0"}]}""",
            ["https://example.test/search?q=json&skip=0&take=10&prerelease=false&semVerLevel=2.0.0"] = """{"data":[{"id":"Newtonsoft.Json","version":"13.0.3","description":"Json.NET","authors":["James"],"totalDownloads":5,"verified":true}]}""",
            ["https://example.test/flat/newtonsoft.json/index.json"] = """{"versions":["12.0.1","13.0.0-beta1","13.0.3"]}""",
        });
        var client = new NuGetClient(new HttpClient(handler), "https://example.test/index.json");

        var results = await client.SearchAsync("json", includePrerelease: false, 0, 10, CancellationToken.None);
        var versions = await client.GetVersionsAsync("Newtonsoft.Json", includePrerelease: false, CancellationToken.None);

        var package = Assert.Single(results);
        Assert.Equal(("Newtonsoft.Json", "13.0.3", "James", true), (package.Id, package.LatestVersion, package.Authors, package.IsVerified));
        Assert.Equal(["13.0.3", "12.0.1"], versions);
    }

    [Fact]
    public void ParsePackageList_ReadsDotnetListJson()
    {
        const string json = """{"version":1,"projects":[{"path":"/a.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Serilog","requestedVersion":"3.0.0","resolvedVersion":"3.0.0","latestVersion":"4.0.0"}]}]}]}""";

        var package = Assert.Single(ProjectService.ParsePackageList(json));

        Assert.True(package.HasUpdate);
        Assert.Equal("4.0.0", package.LatestVersion);
    }

    private sealed class FakeHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
