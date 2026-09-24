namespace Nebula.IntegrationTests.Infrastructure;

/// <summary>
/// Shares one seeded <see cref="NebulaApiFactory"/> across test classes that leave the database as they found it.
/// Classes in this collection run sequentially.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<NebulaApiFactory>
{
    public const string Name = "api";
}
