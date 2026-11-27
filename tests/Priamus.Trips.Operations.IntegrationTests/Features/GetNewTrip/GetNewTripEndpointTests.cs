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

namespace Priamus.Trips.Operations.IntegrationTests.Features.GetNewTrip;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/new-trip</c>, with the registry replaced
/// by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class GetNewTripEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/new-trip";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EarlierToday = new(2026, 6, 15, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterToday = new(2026, 6, 15, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Yesterday = new(2026, 6, 14, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _otherTractor = new(Guid.NewGuid(), "BB222BB", "Volvo FH", TripVehicleKind.Tractor, false);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);

    public GetNewTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Vehicles.AddRange([_tractor, _otherTractor, _trailer]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(NowUtc));
                services.AddSingleton<ITripRegistry>(_registry);
            },
            useTestAuthentication: true);

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    private Task AddAsync(
        DateTime startedAt,
        TripVehicle vehicle,
        TripVehicle? trailer = null,
        Guid? driverId = null) =>
        new TripSeeder(ConnectionString).AddAsync(
            driverId ?? _driverId, _customerId, startedAt, null, null, vehicle.Id, trailer?.Id);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string? PlateOf(JsonElement vehicle) =>
        vehicle.ValueKind == JsonValueKind.Null ? null : vehicle.GetProperty("licensePlate").GetString();

    [Fact]
    public async Task Get_NoTripAtAll_ProposesNothingAndHasNoTripInProgress()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("vehicle").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("hasTripInProgress").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Get_LatestTripStartedTodayWithATrailer_ProposesItsVehicleAndTrailerWithTheirNames()
    {
        // Arrange: the earlier trip of today used another vehicle and must not win.
        await AddAsync(EarlierToday, _otherTractor);
        await AddAsync(LaterToday, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        PlateOf(body.GetProperty("vehicle")).ShouldBe("AA111AA");
        body.GetProperty("vehicle").GetProperty("description").GetString().ShouldBe("Iveco Stralis");
        body.GetProperty("vehicle").GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        PlateOf(body.GetProperty("trailer")).ShouldBe("RR333RR");
    }

    [Fact]
    public async Task Get_LatestTripStartedTodayWithoutATrailer_ProposesTheVehicleAlone()
    {
        // Arrange
        await AddAsync(LaterToday, _otherTractor);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        PlateOf(body.GetProperty("vehicle")).ShouldBe("BB222BB");
        body.GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_LatestTripStartedYesterday_ProposesNothing()
    {
        // Arrange
        await AddAsync(Yesterday, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("vehicle").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TripOfTodayOfAnotherDriver_IsNeverProposed()
    {
        // Arrange
        await AddAsync(LaterToday, _tractor, _trailer, Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("vehicle").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_VehicleNoLongerHolds_ProposesNeitherTheVehicleNorItsTrailer()
    {
        // Arrange
        _registry.AnswersCheck = new TripAnswersCheck(false, true, true, true, true);
        await AddAsync(LaterToday, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("vehicle").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TrailerNoLongerHolds_ProposesTheVehicleWithoutTheTrailer()
    {
        // Arrange
        _registry.AnswersCheck = new TripAnswersCheck(true, false, true, true, true);
        await AddAsync(LaterToday, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        PlateOf(body.GetProperty("vehicle")).ShouldBe("AA111AA");
        body.GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TripStartedLaterButAddedEarlier_IsTheOneTheProposalComesFrom()
    {
        // Arrange: the order of the start times and the order of the adding times are opposite.
        var seeder = new TripSeeder(ConnectionString);
        await seeder.AddInProgressAsync(
            _driverId, _customerId, LaterToday, _tractor.Id, null, addedAt: EarlierToday.AddHours(-1));
        await seeder.AddAsync(_driverId, _customerId, EarlierToday, null, null, _otherTractor.Id);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        PlateOf(body.GetProperty("vehicle")).ShouldBe("AA111AA");
    }

    [Fact]
    public async Task Get_PlannedTripOfToday_IsNotTheTripTheProposalComesFrom()
    {
        // Arrange: the trip added later was never started, so it has no day.
        await AddAsync(EarlierToday, _otherTractor);
        await new TripSeeder(ConnectionString).AddPlannedAsync(_driverId, _customerId, LaterToday);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        PlateOf(body.GetProperty("vehicle")).ShouldBe("BB222BB");
    }

    [Fact]
    public async Task Get_TripInProgress_TellsItAndProposesItsVehicle()
    {
        // Arrange
        await new TripSeeder(ConnectionString).AddInProgressAsync(_driverId, _customerId, LaterToday, _tractor.Id);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("hasTripInProgress").GetBoolean().ShouldBeTrue();
        PlateOf(body.GetProperty("vehicle")).ShouldBe("AA111AA");
    }

    [Fact]
    public async Task Get_TripInProgressStartedYesterday_IsStillTold()
    {
        // Arrange
        await new TripSeeder(ConnectionString).AddInProgressAsync(_driverId, _customerId, Yesterday, _tractor.Id);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("hasTripInProgress").GetBoolean().ShouldBeTrue();
        body.GetProperty("vehicle").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_OnlyEndedAndPlannedTrips_HasNoTripInProgress()
    {
        // Arrange
        await AddAsync(EarlierToday, _tractor);
        await new TripSeeder(ConnectionString).AddPlannedAsync(_driverId, _customerId, LaterToday);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("hasTripInProgress").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Get_TripInProgressOfAnotherDriver_IsNotTold()
    {
        // Arrange
        await new TripSeeder(ConnectionString).AddInProgressAsync(Guid.NewGuid(), _customerId, LaterToday, _tractor.Id);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("hasTripInProgress").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Get_Response_CarriesOnlyTheProposalsAndTheTripInProgress()
    {
        // Arrange
        await AddAsync(LaterToday, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["vehicle", "trailer", "hasTripInProgress"], ignoreOrder: true);
    }
}
