using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.LocationTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListRateableLocations;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/locations/rateable</c>, against the per-test
/// PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ListRateableLocationsEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/locations/rateable";

    public ListRateableLocationsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    [Fact]
    public async Task OnlyActiveLocations_AreOfferedOrderedByNameThenMunicipality()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedLocationAsync(db, "Discarica", "Pescara");
            await SeedLocationAsync(db, "Cantiere", "Teramo");
            await SeedLocationAsync(db, "Cantiere", "Chieti");
            await SeedLocationAsync(db, "Impianto", "Spoltore", deactivated: true);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateArray()
            .Select(l => $"{l.GetProperty("name").GetString()}, {l.GetProperty("municipality").GetString()}")
            .ShouldBe(["Cantiere, Chieti", "Cantiere, Teramo", "Discarica, Pescara"]);
        body[0].GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task MoreLocationsThanAPage_AreAllOffered()
    {
        // Arrange: more than the 20 of a default page.
        await using (var db = CreateDbContext())
        {
            for (var i = 0; i < 25; i++)
            {
                await SeedLocationAsync(db, $"Luogo {i:00}", "Spoltore");
            }
        }

        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url, Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetArrayLength().ShouldBe(25);
    }

    [Fact]
    public async Task DriverCaller_Returns403()
    {
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());

        var response = await client.GetAsync(Url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
