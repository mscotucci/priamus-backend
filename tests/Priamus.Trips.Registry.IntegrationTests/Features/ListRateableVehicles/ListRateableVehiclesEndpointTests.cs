using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListRateableVehicles;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/vehicles/rateable</c>, against the per-test
/// PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ListRateableVehiclesEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/vehicles/rateable";

    public ListRateableVehiclesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    [Fact]
    public async Task OnlyActiveTractorsAndStandaloneVehicles_AreOfferedOrderedByPlate()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedVehicleAsync(db, "ZZ999ZZ", VehicleType.Tractor);
            await SeedVehicleAsync(db, "CC333CC", VehicleType.StandaloneVehicle);
            await SeedVehicleAsync(db, "AA111AA", VehicleType.Tractor);
            await SeedVehicleAsync(db, "BB222BB", VehicleType.Trailer);
            await SeedVehicleAsync(db, "DD444DD", VehicleType.Tractor, decommissioned: true);
            await SeedVehicleAsync(db, "EE555EE", VehicleType.StandaloneVehicle, decommissioned: true);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateArray().Select(v => v.GetProperty("licensePlate").GetString()).ShouldBe(["AA111AA", "CC333CC", "ZZ999ZZ"]);
        body[0].GetProperty("description").GetString().ShouldBe("Iveco Stralis 480");
        body[0].GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
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
