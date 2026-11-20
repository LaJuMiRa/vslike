namespace VsLike.Projects.NuGet;

public sealed record NuGetPackage(string Id, string LatestVersion, string? Description, string? Authors, long TotalDownloads, bool IsVerified, string? ProjectUrl);
