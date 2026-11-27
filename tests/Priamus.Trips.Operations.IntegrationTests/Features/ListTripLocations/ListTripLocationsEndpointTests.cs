using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Contracts;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Features.ListTripLocations;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/locations</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/> and the test authentication scheme.
/// </summary>
public sealed class ListTripLocationsEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/locations";

    private readonly FakeTripRegistry _registry = new();

    public ListTripLocationsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(
            ConnectionString,
            services => services.AddSingleton<ITripRegistry>(_registry),
            useTestAuthentication: true);

    private static HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(Guid.NewGuid()).ApplyTo(factory.CreateClient());

    [Fact]
    public async Task Get_DriverWithADriver_ReturnsTheRegistryLocationsInTheRegistryOrder()
    {
        // Arrange
        var first = new TripLocation(Guid.NewGuid(), "Zeta Depot", "Aosta");
        var second = new TripLocation(Guid.NewGuid(), "Alfa Plant", "Torino");
        _registry.Locations.AddRange([first, second]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateArray()
            .Select(l => (l.GetProperty("id").GetGuid(), l.GetProperty("name").GetString(), l.GetProperty("municipality").GetString()))
            .ShouldBe([(first.Id, "Zeta Depot", "Aosta"), (second.Id, "Alfa Plant", "Torino")]);
    }

    [Fact]
    public async Task Get_WithSearch_PassesTheTrimmedTextToTheRegistry()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?search=%20%20tor%20", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.LocationSearches.ShouldBe(["tor"]);
    }

    [Fact]
    public async Task Get_WithoutSearch_AsksTheRegistryForAllLocations()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.LocationSearches.ShouldBe([null]);
    }

    [Fact]
    public async Task Get_SearchLongerThanTheRegistryAccepts_Returns400AndNeverReachesTheRegistry()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?search={new string('a', 101)}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _registry.LocationSearches.ShouldBeEmpty();
    }
}
