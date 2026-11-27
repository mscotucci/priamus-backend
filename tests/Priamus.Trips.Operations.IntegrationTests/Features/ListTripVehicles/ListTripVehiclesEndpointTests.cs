using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Contracts;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Features.ListTripVehicles;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/vehicles</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/> and the test authentication scheme.
/// </summary>
public sealed class ListTripVehiclesEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/vehicles";

    private readonly FakeTripRegistry _registry = new();
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AB123CD", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _standalone = new(Guid.NewGuid(), "ZZ999ZZ", "Furgone Fiat", TripVehicleKind.StandaloneVehicle, false);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "XA111AA", "Cisterna 30 mc", TripVehicleKind.Trailer, false);

    public ListTripVehiclesEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Vehicles.AddRange([_tractor, _standalone, _trailer]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(
            ConnectionString,
            services => services.AddSingleton<ITripRegistry>(_registry),
            useTestAuthentication: true);

    private static HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(Guid.NewGuid()).ApplyTo(factory.CreateClient());

    [Fact]
    public async Task Get_LeadingRole_ReturnsTheVehiclesTheRegistryGivesForTheLeadingRole()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?role=Leading", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ShouldBe([_tractor.Id, _standalone.Id]);
    }

    [Fact]
    public async Task Get_TrailerRole_ReturnsTheTrailers()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?role=Trailer", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ShouldBe([_trailer.Id]);
    }

    [Fact]
    public async Task Get_Vehicle_CarriesPlateDescriptionKindAndWhetherItRequiresATrailer()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?role=Leading", Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var first = body.EnumerateArray().First();
        first.GetProperty("licensePlate").GetString().ShouldBe("AB123CD");
        first.GetProperty("description").GetString().ShouldBe("Iveco Stralis");
        first.GetProperty("kind").GetString().ShouldBe("Tractor");
        first.GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        var second = body.EnumerateArray().Last();
        second.GetProperty("kind").GetString().ShouldBe("StandaloneVehicle");
        second.GetProperty("requiresTrailer").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Get_WithSearch_PassesTheTrimmedTextToTheRegistry()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?role=Leading&search=%20%20ab1%20", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.VehicleSearches.ShouldBe(["ab1"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?role=Boat")]
    [InlineData("?role=Leading&search=TOOLONG")]
    public async Task Get_RoleMissingOrUnknownOrSearchTooLong_Returns400AndNeverReachesTheRegistry(string query)
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        var url = Url + query.Replace("TOOLONG", new string('a', 101), StringComparison.Ordinal);

        // Act
        var response = await client.GetAsync(url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _registry.VehicleSearches.ShouldBeEmpty();
    }
}
