namespace VsLike.Roslyn.Tests;

[CollectionDefinition(Name)]
public sealed class SampleSolutionCollection : ICollectionFixture<SampleSolutionFixture>
{
    public const string Name = "Sample solution";
}
