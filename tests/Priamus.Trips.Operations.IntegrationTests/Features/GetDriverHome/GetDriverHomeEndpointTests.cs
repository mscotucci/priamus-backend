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

namespace Priamus.Trips.Operations.IntegrationTests.Features.GetDriverHome;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/home</c>, with the registry replaced by
/// <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test authentication
/// scheme.
/// </summary>
public sealed class GetDriverHomeEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/home";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateTime FirstAddedAt = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _alfa = new(Guid.NewGuid(), "Alfa SRL", "00000000001");
    private readonly TripCustomer _beta = new(Guid.NewGuid(), "Beta SPA", null);
    private readonly TripLocation _north = new(Guid.NewGuid(), "Deposito Nord", "Torino");
    private readonly TripLocation _south = new(Guid.NewGuid(), "Piattaforma Sud", "Bari");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _otherTractor = new(Guid.NewGuid(), "BB222BB", "Volvo FH", TripVehicleKind.Tractor, false);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);

    public GetDriverHomeEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Drivers[_driverId] = new TripDriver("Mario", "Rossi");
        _registry.Customers.AddRange([_alfa, _beta]);
        _registry.Locations.AddRange([_north, _south]);
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
        Guid driverId,
        TripCustomer customer,
        int minutesAfterStart,
        TripLocation? pickup = null,
        TripLocation? delivery = null,
        TripVehicle? vehicle = null,
        TripVehicle? trailer = null,
        DateTime? endedAt = null) =>
        new TripSeeder(ConnectionString).AddAsync(
            driverId,
            customer.Id,
            FirstAddedAt.AddMinutes(minutesAfterStart),
            pickup?.Id,
            delivery?.Id,
            (vehicle ?? _tractor).Id,
            trailer?.Id,
            endedAt: endedAt);

    private async Task SaveAsync(Trip trip)
    {
        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
    }

    private Task SeedPlannedAsync(
        TripCustomer customer, DateTime addedAt, Guid? driverId = null, bool deleted = false)
    {
        var trip = TripSeeder.BuildPlanned(
            TripId.New(), driverId ?? _driverId, customer.Id, _tractor.Id, null, null, null, addedAt,
            FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        return SaveAsync(trip);
    }

    private Task SeedInProgressAsync(
        TripCustomer customer, DateOnly day, TimeOnly start, bool deleted = false)
    {
        var trip = TripSeeder.BuildInProgress(
            TripId.New(), _driverId, customer.Id, _tractor.Id, _trailer.Id, _north.Id, _south.Id, day, start,
            NowUtc, FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        return SaveAsync(trip);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string? NameOf(JsonElement location) =>
        location.ValueKind == JsonValueKind.Null ? null : location.GetProperty("name").GetString();

    [Fact]
    public async Task Get_ReturnsTheFirstAndLastNameOfTheDriverOfTheSession()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("firstName").GetString().ShouldBe("Mario");
        body.GetProperty("lastName").GetString().ShouldBe("Rossi");
    }

    [Fact]
    public async Task Get_DriverWithoutTrips_ReturnsAnEmptyList()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Get_DriverTheRegistryDoesNotKnow_Returns404WithTheItalianText()
    {
        // Arrange
        _registry.Drivers.Clear();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("detail").GetString().ShouldBe("Autista non trovato.");
    }

    [Fact]
    public async Task Get_TripsThatDifferInAnyOfTheFive_AreEachListedMostRecentFirst()
    {
        // Arrange: each trip differs from the first by exactly one of customer, pickup, delivery, vehicle, trailer.
        await AddAsync(_driverId, _alfa, 0, _north, _south);
        await AddAsync(_driverId, _beta, 1, _north, _south);
        await AddAsync(_driverId, _alfa, 2, _south, _south);
        await AddAsync(_driverId, _alfa, 3, _north, _north);
        await AddAsync(_driverId, _alfa, 4, _north, _south, _otherTractor);
        await AddAsync(_driverId, _alfa, 5, _north, _south, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        var trips = body.GetProperty("repeatableTrips").EnumerateArray().ToList();
        trips.Count.ShouldBe(6);
        trips[0].GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
        trips[1].GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("BB222BB");
        NameOf(trips[2].GetProperty("delivery")).ShouldBe("Deposito Nord");
        NameOf(trips[3].GetProperty("pickup")).ShouldBe("Piattaforma Sud");
        trips[4].GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Beta SPA");
        trips[5].GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TheSameTripDoneTwice_IsListedOnceAtTheMomentOfTheLatest()
    {
        // Arrange
        await AddAsync(_driverId, _alfa, 0, _north, _south);
        await AddAsync(_driverId, _beta, 1);
        await AddAsync(_driverId, _alfa, 2, _north, _south);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        var companies = body.GetProperty("repeatableTrips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString());
        companies.ShouldBe(["Alfa SRL", "Beta SPA"]);
    }

    [Fact]
    public async Task Get_TripWithOneLocationNoLocationAndNoTrailer_CarriesNullsAndTheNamesOfWhatItHas()
    {
        // Arrange
        await AddAsync(_driverId, _alfa, 0, _north, null);
        await AddAsync(_driverId, _beta, 1);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert: most recent first, so the trip without locations comes first.
        var trips = body.GetProperty("repeatableTrips").EnumerateArray().ToList();
        trips[0].GetProperty("pickup").ValueKind.ShouldBe(JsonValueKind.Null);
        trips[0].GetProperty("delivery").ValueKind.ShouldBe(JsonValueKind.Null);
        trips[0].GetProperty("trailer").ValueKind.ShouldBe(JsonValueKind.Null);
        trips[0].GetProperty("vehicle").GetProperty("description").GetString().ShouldBe("Iveco Stralis");
        trips[1].GetProperty("customer").GetProperty("vatNumber").GetString().ShouldBe("00000000001");
        NameOf(trips[1].GetProperty("pickup")).ShouldBe("Deposito Nord");
        trips[1].GetProperty("pickup").GetProperty("municipality").GetString().ShouldBe("Torino");
        trips[1].GetProperty("delivery").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TripsOfAnotherDriver_AreNeverShown()
    {
        // Arrange
        await AddAsync(Guid.NewGuid(), _alfa, 0, _north, _south);
        await AddAsync(_driverId, _beta, 1);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Beta SPA"]);
    }

    [Fact]
    public async Task Get_RepeatableTrip_CarriesNothingButNamesAndIds()
    {
        // Arrange
        await AddAsync(_driverId, _alfa, 0, _north, _south, _tractor, _trailer);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        var trip = body.GetProperty("repeatableTrips")[0];
        trip.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["customer", "pickup", "delivery", "vehicle", "trailer"], ignoreOrder: true);
        body.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["firstName", "lastName", "inProgress", "planned", "repeatableTrips"], ignoreOrder: true);
    }

    [Fact]
    public async Task Get_VehicleKeptByTheRegistryWhateverItsStatus_IsStillListedWithItsName()
    {
        // Arrange: DescribeAsync answers whatever the status, as the real registry does for past trips.
        await AddAsync(_driverId, _alfa, 0, vehicle: _otherTractor);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips")[0].GetProperty("vehicle").GetProperty("licensePlate")
            .GetString().ShouldBe("BB222BB");
    }

    [Fact]
    public async Task Get_TripInProgress_IsCarriedWithItsStartCustomerLocationsAndVehicles()
    {
        // Arrange
        await SeedInProgressAsync(_alfa, Today, new TimeOnly(9, 30));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        var trip = body.GetProperty("inProgress").GetProperty("trip");
        trip.GetProperty("state").GetString().ShouldBe("InProgress");
        trip.GetProperty("day").GetString().ShouldBe("2026-06-15");
        trip.GetProperty("start").GetString().ShouldBe("09:30:00");
        trip.GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Alfa SRL");
        NameOf(trip.GetProperty("pickup")).ShouldBe("Deposito Nord");
        NameOf(trip.GetProperty("delivery")).ShouldBe("Piattaforma Sud");
        trip.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        trip.GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
    }

    [Fact]
    public async Task Get_NoTripInProgress_CarriesNull()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("inProgress").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(RegistryContracts.TripMeasureKind.StartAndEnd, "Nothing")]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, "Nothing")]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, "Kilograms")]
    [InlineData(RegistryContracts.TripMeasureKind.Distance, "Kilometres")]
    public async Task Get_TripInProgress_TellsWhatItsEndAsksByTheContractOfTheTripDay(
        RegistryContracts.TripMeasureKind measure, string expected)
    {
        // Arrange: the contract of the trip day, not of today, decides.
        var tripDay = Today.AddDays(-1);
        _registry.Measures[(_alfa.Id, tripDay)] = measure;
        _registry.Measures[(_alfa.Id, Today)] = RegistryContracts.TripMeasureKind.StartOnly;
        await SeedInProgressAsync(_alfa, tripDay, new TimeOnly(9, 30));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("inProgress").GetProperty("endAsks").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task Get_TripInProgressWhoseCustomerWasDeactivatedAfterTheStart_StillAsksTheMeasureOfTheContract()
    {
        // Arrange: the registry no longer offers the customer's questions, but the contract of the trip day
        // still prices by weight, so the end asks the kilograms.
        _registry.Measures[(_alfa.Id, Today)] = RegistryContracts.TripMeasureKind.Weight;
        await SeedInProgressAsync(_alfa, Today, new TimeOnly(9, 30));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("inProgress").GetProperty("endAsks").GetString().ShouldBe("Kilograms");
    }

    [Fact]
    public async Task Get_TripInProgressWhoseDayNoContractCoversAnyMore_AsksNothing()
    {
        // Arrange: no question set for the customer and the day.
        await SeedInProgressAsync(_alfa, Today, new TimeOnly(9, 30));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("inProgress").GetProperty("endAsks").GetString().ShouldBe("Nothing");
    }

    [Fact]
    public async Task Get_PlannedTrips_AreListedInTheOrderTheyWereAdded()
    {
        // Arrange
        await SeedPlannedAsync(_beta, NowUtc.AddHours(-1));
        await SeedPlannedAsync(_alfa, NowUtc.AddHours(-3));
        await SeedPlannedAsync(_alfa, NowUtc.AddHours(-2), Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert: the trip of another driver is not there.
        body.GetProperty("planned").EnumerateArray()
            .Select(p => p.GetProperty("trip").GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Alfa SRL", "Beta SPA"]);
        body.GetProperty("planned")[0].GetProperty("trip").GetProperty("state").GetString().ShouldBe("Planned");
    }

    [Fact]
    public async Task Get_NoPlannedTrip_CarriesAnEmptyList()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("planned").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 6)]
    [InlineData(6, 1)]
    [InlineData(7, 0)]
    public async Task Get_PlannedTrip_CarriesTheDaysLeftBeforeItExpires(int daysAgo, int expected)
    {
        // Arrange
        await SeedPlannedAsync(_alfa, NowUtc.AddDays(-daysAgo));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("planned").EnumerateArray().Select(p => p.GetProperty("daysToExpiry").GetInt32())
            .ShouldBe([expected]);
    }

    [Fact]
    public async Task Get_PlannedTripAddedEightDaysAgo_IsInNoList()
    {
        // Arrange
        await SeedPlannedAsync(_alfa, NowUtc.AddDays(-8));
        await SeedPlannedAsync(_beta, NowUtc.AddDays(-7));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert: the expired trip is not planned and is not repeatable either.
        body.GetProperty("planned").EnumerateArray()
            .Select(p => p.GetProperty("trip").GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Beta SPA"]);
        body.GetProperty("repeatableTrips").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Get_DeletedTrips_AreInNoList()
    {
        // Arrange
        await SeedPlannedAsync(_alfa, NowUtc, deleted: true);
        await SeedInProgressAsync(_beta, Today, new TimeOnly(9, 0), deleted: true);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("planned").GetArrayLength().ShouldBe(0);
        body.GetProperty("inProgress").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_TripAddedBeforeAnotherButEndedAfterIt_IsListedFirst()
    {
        // Arrange: alfa was added first and ended last, beta the other way round.
        await AddAsync(_driverId, _alfa, 0, endedAt: FirstAddedAt.AddHours(5));
        await AddAsync(_driverId, _beta, 60, endedAt: FirstAddedAt.AddHours(2));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Alfa SRL", "Beta SPA"]);
    }

    [Fact]
    public async Task Get_TwoTripsEndedAtTheSameMoment_ListsTheOneNumberedLastFirst()
    {
        // Arrange: alfa is saved first (lower number), beta second; both ended together, so only the number
        // tells them apart and the order wanted is the reverse of the order they were saved in.
        var endedAt = FirstAddedAt.AddHours(3);
        await AddAsync(_driverId, _alfa, 0, endedAt: endedAt);
        await AddAsync(_driverId, _beta, 60, endedAt: endedAt);
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Beta SPA", "Alfa SRL"]);
    }

    [Fact]
    public async Task Get_PlannedTripsAndTheTripInProgress_AreNotRepeatableTrips()
    {
        // Arrange
        await AddAsync(_driverId, _alfa, 0, _north, _south);
        await SeedPlannedAsync(_beta, NowUtc);
        await SeedInProgressAsync(_beta, Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var body = await ReadAsync(await DriverClient(factory).GetAsync(Url, Ct));

        // Assert
        body.GetProperty("repeatableTrips").EnumerateArray()
            .Select(t => t.GetProperty("customer").GetProperty("companyName").GetString())
            .ShouldBe(["Alfa SRL"]);
    }
}
