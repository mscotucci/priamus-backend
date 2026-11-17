using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

[CollectionDefinition("RegistryIntegrationTests")]
public sealed class RegistryIntegrationTestsCollection : ICollectionFixture<PostgresFixture>
{
    // Marker class. No body needed.
}
