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

namespace Priamus.Trips.Operations.IntegrationTests.Features.StartTrip;

/// <summary>
/// Endpoint integration tests for <c>POST /api/operations/driver/trips/{tripId}/start</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class StartTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);

    public StartTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.Add(_tractor);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UrlOf(Guid tripId) => $"/api/operations/driver/trips/{tripId}/start";

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

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task<List<Trip>> TripsAsync()
    {
        await using var db = NewContext();
        return await db.Trips.AsNoTracking().ToListAsync(Ct);
    }

    private async Task<TripId> SaveAsync(Trip trip)
    {
        await using var db = NewContext();
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
        return trip.Id;
    }

    private Task<TripId> SeedPlannedAsync(DateTime? addedAt = null, Guid? driverId = null) =>
        SaveAsync(TripSeeder.BuildPlanned(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null,
            addedAt ?? NowUtc, FakeCurrentUser.ActorId));

    private Task<TripId> SeedInProgressAsync(DateOnly day, TimeOnly start) =>
        SaveAsync(TripSeeder.BuildInProgress(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, day, start, NowUtc,
            FakeCurrentUser.ActorId));

    private Task SeedEndedAsync(DateTime startedAt, TimeOnly end) =>
        new TripSeeder(ConnectionString).AddAsync(_driverId, _customer.Id, startedAt, end: end);

    /// <summary>The <c>detail</c> of a problem response; the body stays readable by the other assertions.</summary>
    private static async Task<string> DetailOfAsync(HttpResponseMessage response)
    {
        await response.Content.LoadIntoBufferAsync(Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task Post_NoDayAndNoTime_StartsTheTripNowStampedWithTodayAndTheTimeNow()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("day").GetString().ShouldBe("2026-06-15");
        body.GetProperty("start").GetString().ShouldBe("12:00:00");
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.Day.ShouldBe(Today);
        trip.Start.ShouldBe(new TimeOnly(12, 0));
        _registry.ValidatedAnswers.ShouldHaveSingleItem().Day.ShouldBe(Today);
    }

    [Fact]
    public async Task Post_ADayAndTimeOfYesterday_StartsTheTripThenAndChecksTheAnswersOnThatDay()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-14", time = "09:30" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("day").GetString().ShouldBe("2026-06-14");
        body.GetProperty("start").GetString().ShouldBe("09:30:00");
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.State.ShouldBe(TripState.InProgress);
        trip.Day.ShouldBe(new DateOnly(2026, 6, 14));
        _registry.ValidatedAnswers.ShouldHaveSingleItem().Day.ShouldBe(new DateOnly(2026, 6, 14));
    }

    [Theory]
    [InlineData("2026-06-15", "12:01")]
    [InlineData("2026-06-16", "08:00")]
    public async Task Post_AMomentLaterThanNow_Returns422AndStartsNothing(string day, string time)
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { day, time }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("L'ora non può essere nel futuro.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.InTheFuture, Ct);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_ATimeExactlyNow_StartsTheTrip()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-15", time = "12:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_ATypedTimeWithSecondsInTheCurrentMinute_StartsTheTripStoredAndReturnedWithoutSeconds()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-15", time = "12:00:30" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("start").GetString().ShouldBe("12:00:00");
        (await TripsAsync()).ShouldHaveSingleItem().Start.ShouldBe(new TimeOnly(12, 0));
    }

    [Theory]
    [InlineData("10:30")] // inside the other trip
    [InlineData("10:00")] // exactly its start
    [InlineData("09:00")] // before it: the new trip would be open across it
    public async Task Post_AStartThatOverlapsAnotherTrip_Returns422WithTheOtherTripsTimesAndStartsNothing(string time)
    {
        // Arrange
        await SeedEndedAsync(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), new TimeOnly(11, 0));
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-15", time }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("Hai già un viaggio dalle 10:00 alle 11:00 di quel giorno. Controlla l'ora.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.Overlaps, Ct);
        (await TripsAsync()).Single(t => t.Id == id).State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_AStartExactlyWhenAnotherTripEnded_StartsTheTrip()
    {
        // Arrange
        await SeedEndedAsync(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), new TimeOnly(11, 0));
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-15", time = "11:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_AStartWhereAnotherDriverHasATrip_StartsTheTrip()
    {
        // Arrange
        await new TripSeeder(ConnectionString).AddAsync(
            Guid.NewGuid(), _customer.Id, new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), end: new TimeOnly(11, 0));
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-15", time = "10:30" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_WhileAnotherTripIsInProgress_Returns422AndStartsNothing()
    {
        // Arrange
        await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("Hai un viaggio in corso: termina quello prima di iniziare questo.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.AnotherInProgress, Ct);
        (await TripsAsync()).Single(t => t.Id == id).State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_WhenAnotherStartIsSavedMeanwhile_Returns422AndTheTripStaysPlanned()
    {
        // Arrange: the competing start is saved after the check, before this one is.
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory(() => SeedInProgressAsync(Today, new TimeOnly(9, 0)));

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.AnotherInProgress, Ct);
        (await TripsAsync()).Single(t => t.Id == id).State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_ARegistryFactThatNoLongerHolds_Returns409AndStartsNothing()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-10", time = "08:00" }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, RegistryTripErrors.AnswersChanged, Ct);
        _registry.ValidatedAnswers.ShouldHaveSingleItem().Day.ShouldBe(new DateOnly(2026, 6, 10));
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_OnATripAlreadyStarted_Answers200WithTheTripAsItIsAndChangesNothing()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        await using var factory = CreateFactory();

        // Act: even a new moment, and a registry that would refuse, change nothing.
        var response = await DriverClient(factory).PostAsJsonAsync(
            UrlOf(id.Value), new { day = "2026-06-14", time = "08:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("start").GetString().ShouldBe("09:00:00");
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.Day.ShouldBe(Today);
        trip.Start.ShouldBe(new TimeOnly(9, 0));
    }

    [Theory]
    [InlineData(8, HttpStatusCode.NotFound)] // added on 2026-06-07: expired on 2026-06-15
    [InlineData(7, HttpStatusCode.OK)] // added on 2026-06-08: the last day it can be started
    public async Task Post_OnAPlannedTripAddedLongAgo_IsRefusedOnlyOnceExpired(int daysAgo, HttpStatusCode expected)
    {
        // Arrange
        var id = await SeedPlannedAsync(NowUtc.AddDays(-daysAgo));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.NotFound)
        {
            await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
            (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
        }
    }

    [Fact]
    public async Task Post_OnAPlannedTripAddedEightDaysAgo_Returns404AndTheRowStaysAsItWas()
    {
        // Arrange
        var id = await SeedPlannedAsync(NowUtc.AddDays(-8));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
        await using var db = NewContext();
        var row = (await db.Trips.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct)).ShouldHaveSingleItem();
        row.Id.ShouldBe(id);
        row.State.ShouldBe(TripState.Planned);
        row.Day.ShouldBeNull();
        row.Start.ShouldBeNull();
        row.DeletedAt.ShouldBeNull();
        row.AddedOn.ShouldBe(Today.AddDays(-8));
    }

    [Fact]
    public async Task Post_OnAnUnknownTrip_Returns404()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(Guid.NewGuid()), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
    }

    [Fact]
    public async Task Post_OnAnotherDriversTrip_Returns403AndStartsNothing()
    {
        // Arrange
        var id = await SeedPlannedAsync(driverId: Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TripErrors.OwnedByAnotherDriver, Ct);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Theory]
    [InlineData("""{ "day": "2026-06-14" }""")]
    [InlineData("""{ "time": "09:30" }""")]
    public async Task Post_ADayWithoutATimeOrATimeWithoutADay_Returns400AndStartsNothing(string json)
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await DriverClient(factory).PostAsync(UrlOf(id.Value), content, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
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
