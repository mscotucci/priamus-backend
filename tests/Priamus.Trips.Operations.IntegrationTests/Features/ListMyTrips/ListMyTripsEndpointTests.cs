using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Features.ListMyTrips;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/my-trips</c>, with the registry replaced by
/// <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test authentication
/// scheme.
/// </summary>
public sealed class ListMyTripsEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/my-trips";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _alfa = new(Guid.NewGuid(), "Alfa SRL", "00000000001");
    private readonly TripCustomer _beta = new(Guid.NewGuid(), "Beta SPA", null);
    private readonly TripLocation _north = new(Guid.NewGuid(), "Deposito Nord", "Torino");
    private readonly TripLocation _south = new(Guid.NewGuid(), "Piattaforma Sud", "Bari");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);

    public ListMyTripsEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Drivers[_driverId] = new TripDriver("Mario", "Rossi");
        _registry.Customers.AddRange([_alfa, _beta]);
        _registry.Locations.AddRange([_north, _south]);
        _registry.Vehicles.AddRange([_tractor, _trailer]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(DateTime? nowUtc = null) =>
        ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(nowUtc ?? NowUtc));
                services.AddSingleton<ITripRegistry>(_registry);
            },
            useTestAuthentication: true);

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    /// <summary>Seeds an ended trip of <paramref name="driverId"/> done at <paramref name="at"/> (day and start follow it).</summary>
    private Task AddAsync(
        Guid driverId,
        DateTime at,
        TripCustomer? customer = null,
        TripLocation? pickup = null,
        TripLocation? delivery = null,
        TripVehicle? trailer = null) =>
        new TripSeeder(ConnectionString).AddAsync(
            driverId, (customer ?? _alfa).Id, at, pickup?.Id, delivery?.Id, _tractor.Id, trailer?.Id);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<string?> Customers(JsonElement body) =>
        [.. body.GetProperty("trips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString())];

    [Fact]
    public async Task Get_ReturnsOwnEndedTripsOfTodayAndTheSixDaysBeforeAndNoOthers()
    {
        // Arrange
        await AddAsync(_driverId, NowUtc.AddDays(-7), _alfa);
        await AddAsync(_driverId, NowUtc.AddDays(-6), _beta);
        await AddAsync(_driverId, NowUtc, _alfa);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("today").GetString().ShouldBe("2026-06-15");
        Customers(body).ShouldBe(["Alfa SRL", "Beta SPA"]);
    }

    [Fact]
    public async Task Get_At2330UtcWhenItIsAlreadyTomorrowInItaly_TheWindowFollowsTheItalianDay()
    {
        // Arrange: 23:30 UTC on 2026-06-15 is 01:30 of 2026-06-16 in Italy. The trip done now is on the Italian
        // today; the one done 7 days ago is on the Italian 2026-06-09, outside the window.
        var nowUtc = new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc);
        await AddAsync(_driverId, nowUtc, _alfa);
        await AddAsync(_driverId, nowUtc.AddDays(-7), _beta);
        await using var factory = CreateFactory(nowUtc);

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("today").GetString().ShouldBe("2026-06-16");
        Customers(body).ShouldBe(["Alfa SRL"]);
    }

    [Fact]
    public async Task Get_ListsTheNewestFirstByDayThenByStartWithNumberPlacesPlatesAndTimes()
    {
        // Arrange: two trips today (08:00 and 10:00 in Italy) and one yesterday.
        await AddAsync(_driverId, NowUtc.AddHours(-2), _alfa);
        await AddAsync(_driverId, NowUtc.AddDays(-1), _beta);
        await AddAsync(_driverId, NowUtc, _beta, _north, _south, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        Customers(body).ShouldBe(["Beta SPA", "Alfa SRL", "Beta SPA"]);
        var first = body.GetProperty("trips")[0];
        first.GetProperty("day").GetString().ShouldBe("2026-06-15");
        first.GetProperty("start").GetString().ShouldBe("12:00:00");
        first.GetProperty("end").GetString().ShouldBe("12:01:00");
        first.GetProperty("number").GetInt64().ShouldBeGreaterThan(0);
        first.GetProperty("pickup").GetProperty("name").GetString().ShouldBe("Deposito Nord");
        first.GetProperty("delivery").GetProperty("name").GetString().ShouldBe("Piattaforma Sud");
        first.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        first.GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
        body.GetProperty("trips")[1].GetProperty("start").GetString().ShouldBe("10:00:00");
    }

    [Fact]
    public async Task Get_TripsInProgressAndPlanned_AreNotListed()
    {
        // Arrange
        await AddAsync(_driverId, NowUtc, _alfa);
        var seeder = new TripSeeder(ConnectionString);
        await seeder.AddInProgressAsync(_driverId, _beta.Id, NowUtc, _tractor.Id);
        await seeder.AddPlannedAsync(_driverId, _beta.Id, NowUtc);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        Customers(body).ShouldBe(["Alfa SRL"]);
        body.GetProperty("trips")[0].GetProperty("state").GetString().ShouldBe("Ended");
    }

    [Fact]
    public async Task Get_NeverShowsTripsOfAnotherDriverNorAPrice()
    {
        // Arrange
        await AddAsync(Guid.NewGuid(), NowUtc, _beta);
        await AddAsync(_driverId, NowUtc, _alfa);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        Customers(body).ShouldBe(["Alfa SRL"]);
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["today", "trips"], ignoreOrder: true);
        body.GetProperty("trips")[0].EnumerateObject().Select(p => p.Name).ShouldBe(
            [
                "id", "state", "customer", "pickup", "delivery", "vehicle", "trailer", "day", "start", "end",
                "kilograms", "kilometres", "number",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Get_DriverWithoutRecentTrips_ReturnsAnEmptyList()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("trips").GetArrayLength().ShouldBe(0);
    }
}
