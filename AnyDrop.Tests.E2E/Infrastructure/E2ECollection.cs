namespace AnyDrop.Tests.E2E.Infrastructure;

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2ETestFixture>
{
    public const string Name = "E2E";
}
