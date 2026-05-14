using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Infrastructure;

[CollectionDefinition("IdentityIntegrationTests")]
public sealed class IdentityIntegrationTestsCollection : ICollectionFixture<PostgresFixture>
{
    // Marker class. No body needed.
}
