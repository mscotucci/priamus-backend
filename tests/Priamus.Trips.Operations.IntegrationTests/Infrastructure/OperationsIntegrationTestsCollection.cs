using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Infrastructure;

[CollectionDefinition("OperationsIntegrationTests")]
public sealed class OperationsIntegrationTestsCollection : ICollectionFixture<PostgresFixture>
{
    // Marker class. No body needed.
}
