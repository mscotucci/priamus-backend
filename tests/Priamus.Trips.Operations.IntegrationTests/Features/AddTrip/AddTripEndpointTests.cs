using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
using RegistryTripErrors = Priamus.Trips.Registry.Domain.Trips.TripErrors;

namespace Priamus.Trips.Operations.IntegrationTests.Features.AddTrip;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/operations/driver/trips/{tripId}</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class AddTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);
    private readonly TripLocation _pickup = new(Guid.NewGuid(), "Deposito Nord", "Bergamo");
    private readonly TripLocation _delivery = new(Guid.NewGuid(), "Porto", "Genova");

    public AddTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.AddRange([_tractor, _trailer]);
        _registry.Locations.AddRange([_pickup, _delivery]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UrlOf(Guid tripId) => $"/api/operations/driver/trips/{tripId}";

    private WebApplicationFactory<Program> CreateFactory(Func<Task>? competingSave = null) =>
        ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(NowUtc));
                services.AddSingleton<ITripRegistry>(_registry);
                if (competingSave is not null)
                {
                    services.ConfigureDbContext<OperationsDbContext>(o => o.AddInterceptors(new CompetingSave(competingSave)));
                }
            },
            useTestAuthentication: true);

    private HttpClient DriverClient(WebApplicationFactory<Program> factory, Guid? driverId = null) =>
        TestCaller.DriverWith(driverId ?? _driverId).ApplyTo(factory.CreateClient());

    private object Body(bool start = false, Guid? trailerId = null, bool withLocations = false) => new
    {
        customerId = _customer.Id,
        vehicleId = _tractor.Id,
        trailerId,
        pickupLocationId = withLocations ? _pickup.Id : (Guid?)null,
        deliveryLocationId = withLocations ? _delivery.Id : (Guid?)null,
        start,
    };

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task<List<Trip>> TripsAsync()
    {
        await using var db = NewContext();
        return await db.Trips.AsNoTracking().ToListAsync(Ct);
    }

    private Task SeedInProgressAsync(Guid driverId) =>
        new TripSeeder(ConnectionString).AddInProgressAsync(
            driverId, _customer.Id, new DateTime(2026, 6, 15, 3, 0, 0, DateTimeKind.Utc), _tractor.Id);

    [Fact]
    public async Task Put_TripNotStarted_Returns201WithAPlannedTripWithoutDayAndTime()
    {
        // Arrange
        await using var factory = CreateFactory();
        var tripId = Guid.NewGuid();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(
            UrlOf(tripId), Body(trailerId: _trailer.Id, withLocations: true), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe(UrlOf(tripId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(tripId);
        body.GetProperty("state").GetString().ShouldBe("Planned");
        body.GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Rossi Trasporti Srl");
        body.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        body.GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
        body.GetProperty("pickup").GetProperty("municipality").GetString().ShouldBe("Bergamo");
        body.GetProperty("delivery").GetProperty("name").GetString().ShouldBe("Porto");
        foreach (var name in new[] { "day", "start", "end", "kilograms", "kilometres", "number" })
        {
            body.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null);
        }

        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.State.ShouldBe(TripState.Planned);
        trip.DriverId.ShouldBe(_driverId);
        trip.CustomerId.ShouldBe(_customer.Id);
        trip.LeadingVehicleId.ShouldBe(_tractor.Id);
        trip.TrailerId.ShouldBe(_trailer.Id);
        trip.PickupLocationId.ShouldBe(_pickup.Id);
        trip.DeliveryLocationId.ShouldBe(_delivery.Id);
        trip.Day.ShouldBeNull();
        trip.Start.ShouldBeNull();
        trip.AddedAt.ShouldBe(NowUtc);
        trip.AddedOn.ShouldBe(Today);
        trip.AddedBy.ShouldBe(FakeCurrentUser.ActorId);
    }

    [Fact]
    public async Task Put_SeveralTripsWhileOneIsInProgress_SavesEveryOneAsPlanned()
    {
        // Arrange
        await SeedInProgressAsync(_driverId);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var first = await client.PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(), Ct);
        var second = await client.PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(), Ct);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TripsAsync()).Count(t => t.State == TripState.Planned).ShouldBe(2);
    }

    [Fact]
    public async Task Put_TripStartedNow_Returns201WithATripInProgressStampedWithTodayAndTheTimeNow()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(start: true), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("day").GetString().ShouldBe("2026-06-15");
        body.GetProperty("start").GetString().ShouldBe("12:00:00");
        body.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);

        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.State.ShouldBe(TripState.InProgress);
        trip.Day.ShouldBe(Today);
        trip.Start.ShouldBe(new TimeOnly(12, 0));
    }

    [Fact]
    public async Task Put_StartedNowWhileAnotherIsInProgress_Returns422AndSavesNothing()
    {
        // Arrange
        await SeedInProgressAsync(_driverId);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(start: true), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.AnotherInProgress, Ct);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.InProgress);
    }

    [Fact]
    public async Task Put_StartedNowWhileAnotherDriverHasATripInProgress_Returns201()
    {
        // Arrange
        await SeedInProgressAsync(Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(start: true), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Put_TwoStartsNowThatMeetInTheDatabase_OneIsRefusedWith422AndTheOtherIsSaved()
    {
        // Arrange: the competing start is saved after the check, before this one is.
        await using var factory = CreateFactory(() => SeedInProgressAsync(_driverId));

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(start: true), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.AnotherInProgress, Ct);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.InProgress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_ARegistryFactThatDoesNotHold_Returns409AndSavesNothing(bool start)
    {
        // Arrange
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(start), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, RegistryTripErrors.AnswersChanged, Ct);
        (await TripsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Put_ARegistryFactThatDoesNotHold_AnswersWithTheRegistrysItalianText()
    {
        // Arrange
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(Guid.NewGuid()), Body(), Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("detail").GetString()
            .ShouldBe("Qualcosa è cambiato mentre compilavi. Controlla le risposte e riprova.");
    }

    [Theory]
    [InlineData("""{ "vehicleId": "00000000-0000-0000-0000-000000000001", "start": false }""")]
    [InlineData("""{ "customerId": "00000000-0000-0000-0000-000000000001", "start": false }""")]
    public async Task Put_AMissingCustomerOrVehicle_Returns400AndSavesNothing(string json)
    {
        // Arrange
        await using var factory = CreateFactory();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await DriverClient(factory).PutAsync(UrlOf(Guid.NewGuid()), content, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TripsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Put_TheSameIdentifierTwice_LeavesOneTripAndAnswers200WithIt()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        var tripId = Guid.NewGuid();
        var first = await client.PutAsJsonAsync(UrlOf(tripId), Body(), Ct);

        // Act: the second send asks to start it; the trip as saved is answered and nothing changes.
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        var second = await client.PutAsJsonAsync(UrlOf(tripId), Body(start: true), Ct);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(tripId);
        body.GetProperty("state").GetString().ShouldBe("Planned");
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Put_TheSameIdentifierSavedByACompetingRequest_Answers200WithTheSavedTrip()
    {
        // Arrange: a competing request saves the same identifier after every check of the handler.
        var tripId = Guid.NewGuid();
        await using var factory = CreateFactory(async () =>
        {
            await using var db = NewContext();
            db.Trips.Add(TripSeeder.BuildPlanned(
                new TripId(tripId), _driverId, _customer.Id, _tractor.Id, null, null, null, NowUtc,
                FakeCurrentUser.ActorId));
            await db.SaveChangesAsync(Ct);
        });

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(tripId), Body(), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(tripId);
        (await TripsAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Put_AnIdentifierOfAnotherDriversTrip_Returns403AndChangesNothing()
    {
        // Arrange
        await using var factory = CreateFactory();
        var tripId = Guid.NewGuid();
        var otherDriver = Guid.NewGuid();
        (await DriverClient(factory, otherDriver).PutAsJsonAsync(UrlOf(tripId), Body(), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await DriverClient(factory).PutAsJsonAsync(UrlOf(tripId), Body(start: true), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TripErrors.OwnedByAnotherDriver, Ct);
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.DriverId.ShouldBe(otherDriver);
        trip.State.ShouldBe(TripState.Planned);
    }

    /// <summary>
    /// Runs <paramref name="beforeSave"/> once, just before the handler's trip is written: where a competing
    /// request would have saved its own trip, after every check of the handler has passed.
    /// </summary>
    private sealed class CompetingSave(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        private bool _ran;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_ran)
            {
                _ran = true;
                await beforeSave();
            }

            return result;
        }
    }
}
