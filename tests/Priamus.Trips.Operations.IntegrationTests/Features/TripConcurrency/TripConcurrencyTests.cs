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
using TripMeasureKind = Priamus.Trips.Registry.Contracts.TripMeasureKind;
using TripMinimumKind = Priamus.Trips.Registry.Contracts.TripMinimumKind;
using TripPricing = Priamus.Trips.Registry.Contracts.TripPricing;
using TripRateSource = Priamus.Trips.Registry.Contracts.TripRateSource;

namespace Priamus.Trips.Operations.IntegrationTests.Features.TripConcurrency;

/// <summary>
/// Two acts on the same trip (start, end, delete) that overlap: the competing act is a real request sent
/// just before the handler under test saves, after every check of that handler has passed, so the save of
/// the handler under test is the one that loses the race. The handler answers from the trip as it finds it
/// again, never a 409 for the race itself.
/// </summary>
public sealed class TripConcurrencyTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);

    public TripConcurrencyTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.Add(_tractor);
        _registry.PricedTripResult = new PricedTrip(
            Guid.NewGuid(),
            TripMeasureKind.StartAndEnd,
            [],
            new TripPricing(
                TripRateSource.Contract, null, null, null,
                DayPrice: 60m, NightPrice: 70m, NightCallFee: 5m,
                TripMinimumKind.None, MinimumAmount: null, MinimumThreshold: null, MinimumOveragePrice: null,
                NightBandStart: new TimeOnly(22, 0), NightBandEnd: new TimeOnly(6, 0), NightSurcharge: true,
                IsNight: false, Quantity: 1, UnitPrice: 60m, BaseAmount: 60m, AmountAfterMinimum: 60m,
                MinimumApplied: false, AppliedNightCallFee: null, Total: 60m));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string TripUrl(TripId id) => $"/api/operations/driver/trips/{id.Value}";

    private WebApplicationFactory<Program> CreateFactory(Func<Task> competingSave)
    {
        // One instance for the whole host: the competing request builds its own DbContext, and a context
        // with an interceptor of its own would send a competing request in turn.
        var interceptor = new CompetingSave(competingSave);
        return ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(NowUtc));
                services.AddSingleton<ITripRegistry>(_registry);
                services.ConfigureDbContext<OperationsDbContext>(o => o.AddInterceptors(interceptor));
            },
            useTestAuthentication: true);
    }

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>The only trip of the table, deleted or not.</summary>
    private async Task<Trip> TheTripAsync()
    {
        await using var db = NewContext();
        return await db.Trips.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
    }

    private async Task<TripId> SaveAsync(Trip trip)
    {
        await using var db = NewContext();
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
        return trip.Id;
    }

    private Task<TripId> SeedPlannedAsync() =>
        SaveAsync(TripSeeder.BuildPlanned(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, NowUtc, FakeCurrentUser.ActorId));

    private Task<TripId> SeedInProgressAsync() =>
        SaveAsync(TripSeeder.BuildInProgress(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, Today,
            new TimeOnly(9, 0), NowUtc, FakeCurrentUser.ActorId));

    [Fact]
    public async Task Start_LosingToAnotherStart_Returns200WithTheTripAsTheWinnerLeftItAndOneEffect()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        var versionBefore = (await TheTripAsync()).Version;
        HttpClient? client = null;
        await using var factory = CreateFactory(() =>
            client!.PostAsJsonAsync(TripUrl(id) + "/start", new { day = "2026-06-14", time = "09:30" }, Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.PostAsJsonAsync(TripUrl(id) + "/start", new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("day").GetString().ShouldBe("2026-06-14");
        body.GetProperty("start").GetString().ShouldBe("09:30:00");
        var trip = await TheTripAsync();
        trip.Day.ShouldBe(new DateOnly(2026, 6, 14));
        trip.Start.ShouldBe(new TimeOnly(9, 30));
        trip.Version.ShouldBe(versionBefore + 1);
    }

    [Fact]
    public async Task End_LosingToAnotherEnd_Returns200WithTheTripAsTheWinnerLeftItAndTheWinnersNumber()
    {
        // Arrange
        var id = await SeedInProgressAsync();
        var versionBefore = (await TheTripAsync()).Version;
        HttpClient? client = null;
        await using var factory = CreateFactory(() =>
            client!.PostAsJsonAsync(TripUrl(id) + "/end", new { time = "11:00" }, Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.PostAsJsonAsync(TripUrl(id) + "/end", new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trip = await TheTripAsync();
        trip.State.ShouldBe(TripState.Ended);
        trip.End.ShouldBe(new TimeOnly(11, 0));
        trip.Number.ShouldNotBeNull();
        trip.Version.ShouldBe(versionBefore + 1);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("end").GetString().ShouldBe("11:00:00");
        body.GetProperty("number").GetInt64().ShouldBe(trip.Number.Value);
    }

    [Fact]
    public async Task Delete_LosingToAnotherDelete_Returns204AndTheTripIsMarkedOnce()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        var versionBefore = (await TheTripAsync()).Version;
        HttpClient? client = null;
        await using var factory = CreateFactory(() => client!.DeleteAsync(TripUrl(id), Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.DeleteAsync(TripUrl(id), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var trip = await TheTripAsync();
        trip.DeletedAt.ShouldBe(NowUtc);
        trip.Version.ShouldBe(versionBefore + 1);
    }

    [Fact]
    public async Task Start_LosingToADelete_Returns404AndTheTripStaysPlanned()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        HttpClient? client = null;
        await using var factory = CreateFactory(() => client!.DeleteAsync(TripUrl(id), Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.PostAsJsonAsync(TripUrl(id) + "/start", new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
        var trip = await TheTripAsync();
        trip.DeletedAt.ShouldNotBeNull();
        trip.State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task End_LosingToADelete_Returns404AndTheTripIsNotEnded()
    {
        // Arrange
        var id = await SeedInProgressAsync();
        HttpClient? client = null;
        await using var factory = CreateFactory(() => client!.DeleteAsync(TripUrl(id), Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.PostAsJsonAsync(TripUrl(id) + "/end", new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
        var trip = await TheTripAsync();
        trip.DeletedAt.ShouldNotBeNull();
        trip.State.ShouldBe(TripState.InProgress);
        trip.Number.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_LosingToAnEnd_IsRefusedAsAnEndedTripAndTheTripStaysEnded()
    {
        // Arrange
        var id = await SeedInProgressAsync();
        HttpClient? client = null;
        await using var factory = CreateFactory(() =>
            client!.PostAsJsonAsync(TripUrl(id) + "/end", new { time = "11:00" }, Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.DeleteAsync(TripUrl(id), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.EndedTripCannotBeDeleted, Ct);
        var trip = await TheTripAsync();
        trip.State.ShouldBe(TripState.Ended);
        trip.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_LosingToAStart_ReadsTheTripAgainAndDeletesIt()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        HttpClient? client = null;
        await using var factory = CreateFactory(() => client!.PostAsJsonAsync(TripUrl(id) + "/start", new { }, Ct));
        client = DriverClient(factory);

        // Act
        var response = await client.DeleteAsync(TripUrl(id), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var trip = await TheTripAsync();
        trip.DeletedAt.ShouldBe(NowUtc);
        trip.State.ShouldBe(TripState.InProgress);
    }

    /// <summary>
    /// Runs <paramref name="beforeSave"/> once, just before the handler's trip is written: where a competing
    /// request would have saved its own change, after every check of the handler has passed.
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
