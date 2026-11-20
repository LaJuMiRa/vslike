namespace VsLike.Projects.Tests;

public class NamespaceResolverTests
{
    [Theory]
    [InlineData("/r/Shop.App/Shop.App.csproj", null, "/r/Shop.App", "Shop.App")]
    [InlineData("/r/Shop.App/Shop.App.csproj", "Contoso.Shop", "/r/Shop.App/Models/Orders", "Contoso.Shop.Models.Orders")]
    [InlineData("/r/my-app/my-app.csproj", null, "/r/my-app/2d models", "my_app._2d_models")]
    public void Resolve_CombinesRootNamespaceAndFolders(string project, string? root, string folder, string expected)
    {
        Assert.Equal(expected, NamespaceResolver.Resolve(project, root, folder));
    }

    [Fact]
    public void ClassFile_UsesFileScopedNamespace()
    {
        Assert.Equal("namespace Shop.Models;\n\npublic class Order\n{\n}\n", NamespaceResolver.ClassFile("Shop.Models", "Order"));
    }
}
