using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.IntegrationTests.Features.GetTrip;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/trips/{tripId}</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class GetTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private long _lastNumber;
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);

    public GetTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.Add(_tractor);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UrlOf(TripId id) => $"/api/operations/driver/trips/{id.Value}";

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

    private async Task<TripId> SaveAsync(Trip trip)
    {
        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
        return trip.Id;
    }

    private Task<TripId> SeedPlannedAsync(DateTime? addedAt = null, Guid? driverId = null, bool deleted = false)
    {
        var trip = TripSeeder.BuildPlanned(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null,
            addedAt ?? NowUtc, FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        return SaveAsync(trip);
    }

    private Task<TripId> SeedInProgressAsync(DateOnly day, TimeOnly start) =>
        SaveAsync(TripSeeder.BuildInProgress(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, day, start, NowUtc,
            FakeCurrentUser.ActorId));

    private Task SeedEndedAsync(DateOnly day, TimeOnly start, TimeOnly end, Guid? driverId = null) =>
        SaveAsync(TripSeeder.Build(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null, day, start, end,
            null, null, PricedStartAndEnd(), ++_lastNumber, NowUtc, FakeCurrentUser.ActorId));

    private static RegistryContracts.PricedTrip PricedStartAndEnd() =>
        new(
            Guid.NewGuid(),
            RegistryContracts.TripMeasureKind.StartAndEnd,
            [],
            new RegistryContracts.TripPricing(
                RegistryContracts.TripRateSource.Contract, null, null, null, 60m, 70m, 5m,
                RegistryContracts.TripMinimumKind.None, null, null, null, new TimeOnly(22, 0), new TimeOnly(6, 0),
                true, false, 1, 60m, 60m, 60m, false, null, 60m));

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Get_APlannedTrip_ReturnsItWithTheNamesOfWhatItRefersTo()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        var trip = body.GetProperty("trip");
        trip.GetProperty("id").GetGuid().ShouldBe(id.Value);
        trip.GetProperty("state").GetString().ShouldBe("Planned");
        trip.GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Rossi Trasporti Srl");
        trip.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        trip.GetProperty("day").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("endAsks").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(3, 4)]
    [InlineData(7, 0)]
    public async Task Get_APlannedTrip_CarriesTheDaysLeftBeforeItExpires(int daysAgo, int expected)
    {
        // Arrange
        var id = await SeedPlannedAsync(NowUtc.AddDays(-daysAgo));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("daysToExpiry").GetInt32().ShouldBe(expected);
    }

    [Fact]
    public async Task Get_APlannedTrip_ProposesTheEndTimeOfTheLatestTripEndedToday()
    {
        // Arrange: the latest of today ended at 11:15; a trip of another driver ended later and one of
        // yesterday ended earlier in the day than both.
        var id = await SeedPlannedAsync();
        await SeedEndedAsync(Today, new TimeOnly(6, 0), new TimeOnly(8, 0));
        await SeedEndedAsync(Today, new TimeOnly(9, 0), new TimeOnly(11, 15));
        await SeedEndedAsync(Today, new TimeOnly(9, 0), new TimeOnly(11, 50), Guid.NewGuid());
        await SeedEndedAsync(Today.AddDays(-1), new TimeOnly(10, 0), new TimeOnly(11, 59));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("proposedStart").GetString().ShouldBe("11:15:00");
    }

    [Fact]
    public async Task Get_APlannedTripAfterATripThatEndedPastMidnight_ProposesItsEndTime()
    {
        // Arrange: started yesterday at 22:00, ended today at 00:40.
        var id = await SeedPlannedAsync();
        await SeedEndedAsync(Today.AddDays(-1), new TimeOnly(22, 0), new TimeOnly(0, 40));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("proposedStart").GetString().ShouldBe("00:40:00");
    }

    [Theory]
    [InlineData(23, 0, 1, 0, 6, 0, 7, 30, "07:30:00")] // the one started today ends later
    [InlineData(23, 0, 7, 0, 3, 0, 4, 0, "07:00:00")]  // the one started yesterday ends later
    public async Task Get_APlannedTripWithATripEndedPastMidnightAndOneStartedToday_ProposesTheLaterEnd(
        int yesterdayStartHour, int yesterdayStartMinute, int yesterdayEndHour, int yesterdayEndMinute,
        int todayStartHour, int todayStartMinute, int todayEndHour, int todayEndMinute, string expected)
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await SeedEndedAsync(
            Today.AddDays(-1),
            new TimeOnly(yesterdayStartHour, yesterdayStartMinute),
            new TimeOnly(yesterdayEndHour, yesterdayEndMinute));
        await SeedEndedAsync(
            Today,
            new TimeOnly(todayStartHour, todayStartMinute),
            new TimeOnly(todayEndHour, todayEndMinute));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("proposedStart").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task Get_APlannedTripWhenNoTripEndedToday_ProposesNothing()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await SeedEndedAsync(Today.AddDays(-1), new TimeOnly(9, 0), new TimeOnly(11, 0));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("proposedStart").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(RegistryContracts.TripMeasureKind.StartAndEnd, "Nothing")]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, "Kilograms")]
    [InlineData(RegistryContracts.TripMeasureKind.Distance, "Kilometres")]
    public async Task Get_ATripInProgress_TellsWhatItsEndAsksByTheContractOfTheTripDay(
        RegistryContracts.TripMeasureKind measure, string expected)
    {
        // Arrange
        _registry.Measures[(_customer.Id, Today)] = measure;
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("trip").GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("endAsks").GetString().ShouldBe(expected);
        body.GetProperty("daysToExpiry").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("proposedStart").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_ATripInProgressWhoseCustomerWasDeactivatedAfterTheStart_StillAsksTheMeasureOfTheContract()
    {
        // Arrange: the registry no longer offers the customer's questions, but the contract of the trip day
        // still prices by weight, so the end asks the kilograms.
        _registry.Measures[(_customer.Id, Today)] = RegistryContracts.TripMeasureKind.Weight;
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("endAsks").GetString().ShouldBe("Kilograms");
    }

    [Fact]
    public async Task Get_ATripInProgressWhoseDayNoContractCoversAnyMore_AsksNothing()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.GetProperty("endAsks").GetString().ShouldBe("Nothing");
    }

    [Fact]
    public async Task Get_AnEndedTrip_ReturnsItWithItsEndAndNumberAndNothingElse()
    {
        // Arrange
        await SeedEndedAsync(Today, new TimeOnly(9, 0), new TimeOnly(11, 0));
        Guid id;
        await using (var db = new OperationsDbContext(
                         new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options))
        {
            id = (await db.Trips.AsNoTracking().SingleAsync(Ct)).Id.Value;
        }

        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(new TripId(id)), Ct));

        // Assert
        body.GetProperty("trip").GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("trip").GetProperty("end").GetString().ShouldBe("11:00:00");
        body.GetProperty("trip").GetProperty("number").GetInt64().ShouldBe(1);
        body.GetProperty("daysToExpiry").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("endAsks").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_ATripOfAnotherDriver_Returns403()
    {
        // Arrange
        var id = await SeedPlannedAsync(driverId: Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlOf(id), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AnUnknownTrip_Returns404WithTheItalianText()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlOf(TripId.New()), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("detail").GetString().ShouldBe("Viaggio non trovato.");
    }

    [Theory]
    [InlineData(7, HttpStatusCode.OK)]
    [InlineData(8, HttpStatusCode.NotFound)]
    public async Task Get_APlannedTripAddedSevenOrEightDaysAgo_IsExpiredOnlyFromTheEighth(
        int daysAgo, HttpStatusCode expected)
    {
        // Arrange
        var id = await SeedPlannedAsync(NowUtc.AddDays(-daysAgo));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlOf(id), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Get_ADeletedTrip_Returns404()
    {
        // Arrange
        var id = await SeedPlannedAsync(deleted: true);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlOf(id), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ATrip_CarriesNothingButTheTripItsExpiryItsProposalAndWhatItsEndAsks()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(UrlOf(id), Ct));

        // Assert
        body.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["trip", "daysToExpiry", "proposedStart", "endAsks"], ignoreOrder: true);
        body.GetProperty("trip").EnumerateObject().Select(p => p.Name)
            .ShouldBe(
                ["id", "state", "customer", "pickup", "delivery", "vehicle", "trailer", "day", "start", "end",
                 "kilograms", "kilometres", "number"],
                ignoreOrder: true);
    }
}
