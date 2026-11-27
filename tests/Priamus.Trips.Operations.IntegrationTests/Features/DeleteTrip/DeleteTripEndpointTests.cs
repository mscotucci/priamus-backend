using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryTripErrors = Priamus.Trips.Registry.Domain.Trips.TripErrors;

namespace Priamus.Trips.Operations.IntegrationTests.Features.DeleteTrip;

/// <summary>
/// Endpoint integration tests for <c>DELETE /api/operations/driver/trips/{tripId}</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class DeleteTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);

    public DeleteTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.Add(_tractor);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UrlOf(Guid tripId) => $"/api/operations/driver/trips/{tripId}";

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

    private HttpClient DriverClient(WebApplicationFactory<Program> factory, Guid? driverId = null) =>
        TestCaller.DriverWith(driverId ?? _driverId).ApplyTo(factory.CreateClient());

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Every trip of the table, deleted ones included.</summary>
    private async Task<List<Trip>> TripsAsync()
    {
        await using var db = NewContext();
        return await db.Trips.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
    }

    private async Task<TripId> SaveAsync(Trip trip)
    {
        await using var db = NewContext();
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
        return trip.Id;
    }

    private Task<TripId> SeedPlannedAsync(Guid? driverId = null, DateTime? addedAt = null) =>
        SaveAsync(TripSeeder.BuildPlanned(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null, addedAt ?? NowUtc,
            FakeCurrentUser.ActorId));

    private Task<TripId> SeedInProgressAsync() =>
        SaveAsync(TripSeeder.BuildInProgress(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, Today,
            new TimeOnly(9, 0), NowUtc, FakeCurrentUser.ActorId));

    private async Task<TripId> SeedDeletedAsync(DateTime deletedAt, UserId deletedBy, Guid? driverId = null)
    {
        var trip = TripSeeder.BuildPlanned(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null, NowUtc, FakeCurrentUser.ActorId);
        trip.Delete(deletedAt, deletedBy).IsSuccess.ShouldBeTrue();
        return await SaveAsync(trip);
    }

    /// <summary>Reads the deletion columns of the trip's row with raw SQL, so the check skips the EF model.</summary>
    private async Task<(DateTime? DeletedAt, Guid? DeletedBy)> DeletionColumnsAsync(TripId id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT deleted_at, deleted_by FROM operations.trips WHERE id = @id";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return (
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    [Fact]
    public async Task Delete_APlannedTrip_Returns204AndKeepsTheRowMarkedWithWhenAndByWhom()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBe(NowUtc);
        deletedBy.ShouldBe(FakeCurrentUser.ActorId.Value);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Delete_ATripInProgress_Returns204AndKeepsTheRowMarkedWithWhenAndByWhom()
    {
        // Arrange
        var id = await SeedInProgressAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBe(NowUtc);
        deletedBy.ShouldBe(FakeCurrentUser.ActorId.Value);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.InProgress);
    }

    [Fact]
    public async Task Delete_AnEndedTrip_Returns409AndChangesNothing()
    {
        // Arrange
        await new TripSeeder(ConnectionString).AddAsync(
            _driverId, _customer.Id, new DateTime(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc), leadingVehicleId: _tractor.Id);
        var id = (await TripsAsync()).ShouldHaveSingleItem().Id;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.EndedTripCannotBeDeleted, Ct);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBeNull();
        deletedBy.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_ATripOfAnotherDriver_Returns403AndChangesNothing()
    {
        // Arrange
        var id = await SeedPlannedAsync(Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TripErrors.OwnedByAnotherDriver, Ct);
        (await DeletionColumnsAsync(id)).DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_ADeletedTripOfAnotherDriver_Returns403AndChangesNothing()
    {
        // Arrange
        var earlier = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);
        var otherUser = new UserId(Guid.NewGuid());
        var id = await SeedDeletedAsync(earlier, otherUser, Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TripErrors.OwnedByAnotherDriver, Ct);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBe(earlier);
        deletedBy.ShouldBe(otherUser.Value);
    }

    [Fact]
    public async Task Delete_AnExpiredPlannedTrip_Returns204AndMarksTheRowDeleted()
    {
        // Arrange: added 8 days ago, so expired today.
        var id = await SeedPlannedAsync(addedAt: NowUtc.AddDays(-8));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBe(NowUtc);
        deletedBy.ShouldBe(FakeCurrentUser.ActorId.Value);
    }

    [Fact]
    public async Task Delete_TheSameTripTwice_Answers204BothTimesAndTheSecondChangesNothing()
    {
        // Arrange: a trip deleted earlier by someone else, so a second marking would be seen.
        var earlier = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);
        var otherUser = new UserId(Guid.NewGuid());
        var id = await SeedDeletedAsync(earlier, otherUser);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var first = await client.DeleteAsync(UrlOf(id.Value), Ct);
        var second = await client.DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (deletedAt, deletedBy) = await DeletionColumnsAsync(id);
        deletedAt.ShouldBe(earlier);
        deletedBy.ShouldBe(otherUser.Value);
    }

    [Fact]
    public async Task Delete_ATripThatDoesNotExist_Returns204AndCreatesNothing()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).DeleteAsync(UrlOf(Guid.NewGuid()), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TripsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_TheTripInProgress_LetsTheDriverStartAnotherAndNoReadShowsTheDeletedOne()
    {
        // Arrange
        var inProgress = await SeedInProgressAsync();
        var planned = await SeedPlannedAsync();
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        var blocked = await client.PostAsJsonAsync($"{UrlOf(planned.Value)}/start", new { }, Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Act
        var deleted = await client.DeleteAsync(UrlOf(inProgress.Value), Ct);
        var newTrip = await client.GetFromJsonAsync<JsonElement>("/api/operations/driver/new-trip", Ct);
        var started = await client.PostAsJsonAsync($"{UrlOf(planned.Value)}/start", new { }, Ct);
        var startedDeleted = await client.PostAsJsonAsync($"{UrlOf(inProgress.Value)}/start", new { }, Ct);

        // Assert
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        newTrip.GetProperty("hasTripInProgress").GetBoolean().ShouldBeFalse();
        started.StatusCode.ShouldBe(HttpStatusCode.OK);
        startedDeleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ATripInProgressThatCannotBeEnded_Returns204()
    {
        // Arrange
        var id = await SeedInProgressAsync();
        _registry.PricedTripResult = RegistryTripErrors.NotPriceable;
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        var ended = await client.PostAsJsonAsync($"{UrlOf(id.Value)}/end", new { }, Ct);
        ended.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Act
        var response = await client.DeleteAsync(UrlOf(id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DeletionColumnsAsync(id)).DeletedAt.ShouldBe(NowUtc);
    }

    [Fact]
    public async Task Put_TheIdentifierOfADeletedTrip_Returns409AndDoesNotReviveIt()
    {
        // Arrange
        var id = await SeedPlannedAsync();
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        (await client.DeleteAsync(UrlOf(id.Value), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        var response = await client.PutAsJsonAsync(
            UrlOf(id.Value), new { customerId = _customer.Id, vehicleId = _tractor.Id, start = false }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.Deleted, Ct);
        (await TripsAsync()).ShouldHaveSingleItem();
        (await DeletionColumnsAsync(id)).DeletedAt.ShouldBe(NowUtc);
    }
}
