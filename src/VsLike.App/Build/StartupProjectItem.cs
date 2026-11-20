namespace VsLike.App.Build;

public sealed record StartupProjectItem(string Name, string Path)
{
    public override string ToString() => Name;
}
