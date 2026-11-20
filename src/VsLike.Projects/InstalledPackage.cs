namespace VsLike.Projects;

public sealed record InstalledPackage(string Id, string? RequestedVersion, string? ResolvedVersion, string? LatestVersion)
{
    public bool HasUpdate => LatestVersion is not null && ResolvedVersion is not null && LatestVersion != ResolvedVersion;
}
