using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.IntegrationTests.Persistence;

/// <summary>
/// Persistence tests of the <see cref="Trip"/> aggregate against the migrated <c>operations.trips</c>
/// table of a per-test database.
/// </summary>
public sealed class TripPersistenceTests : OperationsIntegrationTestBase
{
    private static readonly Guid DriverId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly DateTime AddedAt = new(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc);

    public TripPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    private static RegistryContracts.TripPricing Pricing(
        RegistryContracts.TripMinimumKind minimum = RegistryContracts.TripMinimumKind.TieredOverage) => new(
        RegistryContracts.TripRateSource.LocationPair,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DayPrice: 50.25m,
        NightPrice: 60.5m,
        NightCallFee: 15m,
        minimum,
        MinimumAmount: minimum == RegistryContracts.TripMinimumKind.None ? null : 200m,
        MinimumThreshold: minimum == RegistryContracts.TripMinimumKind.TieredOverage ? 4m : null,
        MinimumOveragePrice: minimum == RegistryContracts.TripMinimumKind.TieredOverage ? 45.75m : null,
        NightBandStart: new TimeOnly(22, 0),
        NightBandEnd: new TimeOnly(6, 0),
        NightSurcharge: true,
        IsNight: true,
        Quantity: 300,
        UnitPrice: 60.5m,
        BaseAmount: 300.1m,
        AmountAfterMinimum: 310.2m,
        MinimumApplied: minimum != RegistryContracts.TripMinimumKind.None,
        AppliedNightCallFee: 15m,
        Total: 325.3m);

    private static Trip NewEndedTrip(
        Guid? driverId = null,
        long number = 1,
        TimeOnly? start = null,
        RegistryContracts.TripMeasureKind measure = RegistryContracts.TripMeasureKind.StartOnly,
        RegistryContracts.TripMinimumKind minimum = RegistryContracts.TripMinimumKind.None,
        bool withTrailerAndLocations = true)
    {
        var priced = new RegistryContracts.PricedTrip(
            Guid.NewGuid(),
            measure,
            ["DeliveryNote", "WeighingTicket"],
            Pricing(minimum));

        return TripSeeder.Build(
            TripId.New(),
            driverId ?? DriverId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            withTrailerAndLocations ? Guid.NewGuid() : null,
            withTrailerAndLocations ? Guid.NewGuid() : null,
            withTrailerAndLocations ? Guid.NewGuid() : null,
            Day,
            start ?? new TimeOnly(8, 30),
            measure == RegistryContracts.TripMeasureKind.StartAndEnd ? new TimeOnly(12, 15) : null,
            measure == RegistryContracts.TripMeasureKind.Weight ? 18_500 : null,
            measure == RegistryContracts.TripMeasureKind.Distance ? 240 : null,
            priced,
            number,
            AddedAt,
            new UserId(Guid.NewGuid()));
    }

    private static Trip NewPlannedTrip(Guid? driverId = null) => TripSeeder.BuildPlanned(
        TripId.New(),
        driverId ?? DriverId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        AddedAt,
        new UserId(Guid.NewGuid()));

    private static Trip NewTripInProgress(Guid? driverId = null) => TripSeeder.BuildInProgress(
        TripId.New(),
        driverId ?? DriverId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        null,
        null,
        Day,
        new TimeOnly(8, 30),
        AddedAt,
        new UserId(Guid.NewGuid()));

    private async Task SaveAsync(Trip trip)
    {
        await using var writer = NewContext();
        writer.Trips.Add(trip);
        await writer.SaveChangesAsync(Ct);
    }

    private async Task<Trip> SaveAndReadBackAsync(Trip trip)
    {
        await SaveAsync(trip);

        await using var reader = NewContext();
        return await reader.Trips.AsNoTracking().SingleAsync(t => t.Id == trip.Id, Ct);
    }

    [Theory]
    [InlineData(RegistryContracts.TripMeasureKind.StartAndEnd, RegistryContracts.TripMinimumKind.None, true)]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, RegistryContracts.TripMinimumKind.FixedMinimum, true)]
    [InlineData(RegistryContracts.TripMeasureKind.Distance, RegistryContracts.TripMinimumKind.TieredOverage, false)]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, RegistryContracts.TripMinimumKind.None, false)]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, RegistryContracts.TripMinimumKind.TieredOverage, true)]
    public async Task EndedTrip_ReadBack_KeepsEveryMember(
        RegistryContracts.TripMeasureKind measure,
        RegistryContracts.TripMinimumKind minimum,
        bool withTrailerAndLocations)
    {
        // Arrange
        var trip = NewEndedTrip(measure: measure, minimum: minimum, withTrailerAndLocations: withTrailerAndLocations);

        // Act
        var read = await SaveAndReadBackAsync(trip);

        // Assert
        read.Id.ShouldBe(trip.Id);
        read.DriverId.ShouldBe(trip.DriverId);
        read.CustomerId.ShouldBe(trip.CustomerId);
        read.ContractId.ShouldBe(trip.ContractId);
        read.LeadingVehicleId.ShouldBe(trip.LeadingVehicleId);
        read.TrailerId.ShouldBe(trip.TrailerId);
        read.PickupLocationId.ShouldBe(trip.PickupLocationId);
        read.DeliveryLocationId.ShouldBe(trip.DeliveryLocationId);
        read.Day.ShouldBe(trip.Day);
        read.Start.ShouldBe(trip.Start);
        read.End.ShouldBe(trip.End);
        read.EndedAt.ShouldBe(trip.EndedAt);
        read.Number.ShouldBe(trip.Number);
        read.Kilograms.ShouldBe(trip.Kilograms);
        read.Kilometres.ShouldBe(trip.Kilometres);
        read.MeasureKind.ShouldBe(trip.MeasureKind);
        read.Pricing.ShouldBe(trip.Pricing);
        read.RequiredDocuments.ShouldBe(["DeliveryNote", "WeighingTicket"]);
        read.AddedAt.ShouldBe(trip.AddedAt);
        read.AddedOn.ShouldBe(trip.AddedOn);
        read.AddedBy.ShouldBe(trip.AddedBy);
        read.State.ShouldBe(TripState.Ended);
    }

    [Fact]
    public async Task PlannedTrip_ReadBack_HasNoDayTimeContractPricingOrNumber()
    {
        // Arrange
        var trip = NewPlannedTrip();

        // Act
        var read = await SaveAndReadBackAsync(trip);

        // Assert
        read.State.ShouldBe(TripState.Planned);
        read.PickupLocationId.ShouldBe(trip.PickupLocationId);
        read.AddedOn.ShouldBe(trip.AddedOn);
        read.Day.ShouldBeNull();
        read.Start.ShouldBeNull();
        read.End.ShouldBeNull();
        read.EndedAt.ShouldBeNull();
        read.ContractId.ShouldBeNull();
        read.MeasureKind.ShouldBeNull();
        read.Pricing.ShouldBeNull();
        read.Number.ShouldBeNull();
        read.RequiredDocuments.ShouldBeEmpty();
    }

    [Fact]
    public async Task TripInProgress_ReadBack_HasDayAndStartAndNothingElse()
    {
        // Arrange
        var trip = NewTripInProgress();

        // Act
        var read = await SaveAndReadBackAsync(trip);

        // Assert
        read.State.ShouldBe(TripState.InProgress);
        read.Day.ShouldBe(Day);
        read.Start.ShouldBe(new TimeOnly(8, 30));
        read.End.ShouldBeNull();
        read.Pricing.ShouldBeNull();
        read.Number.ShouldBeNull();
    }

    [Fact]
    public async Task PlannedTripAndTripInProgress_HaveNoNumber()
    {
        // Arrange
        await SaveAsync(NewPlannedTrip());
        await SaveAsync(NewTripInProgress(driverId: Guid.NewGuid()));

        // Act
        await using var reader = NewContext();
        var numbers = await reader.Trips.AsNoTracking().Select(t => t.Number).ToListAsync(Ct);

        // Assert
        numbers.Count.ShouldBe(2);
        numbers.ShouldAllBe(n => n == null);
    }

    [Fact]
    public async Task EndedTrips_TakeGrowingNumbersFromTheSequence()
    {
        // Arrange
        await using var db = NewContext();
        var first = await db.NextTripNumberAsync(Ct);
        var second = await db.NextTripNumberAsync(Ct);
        var third = await db.NextTripNumberAsync(Ct);

        // Act
        await SaveAsync(NewEndedTrip(number: first, start: new TimeOnly(6, 0)));
        await SaveAsync(NewEndedTrip(number: second, start: new TimeOnly(7, 0)));
        await SaveAsync(NewEndedTrip(number: third, start: new TimeOnly(8, 0)));

        // Assert
        second.ShouldBeGreaterThan(first);
        third.ShouldBeGreaterThan(second);

        await using var reader = NewContext();
        var stored = await reader.Trips.AsNoTracking().OrderBy(t => t.Number).Select(t => t.Number).ToListAsync(Ct);
        stored.ShouldBe([first, second, third]);
    }

    [Fact]
    public async Task TwoEndedTrips_WithTheSameNumber_AreRefusedByTheDatabase()
    {
        // Arrange
        await SaveAsync(NewEndedTrip(number: 7));

        // Act
        await using var other = NewContext();
        other.Trips.Add(NewEndedTrip(number: 7));
        var exception = await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync(Ct));

        // Assert
        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_trips_number");
    }

    [Fact]
    public async Task SecondTripInProgress_OfTheSameDriver_IsRefusedByTheDatabase()
    {
        // Arrange
        await SaveAsync(NewTripInProgress());

        // Act
        await using var other = NewContext();
        other.Trips.Add(NewTripInProgress());
        var exception = await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync(Ct));

        // Assert
        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_trips_driver_id_in_progress");

        await using var reader = NewContext();
        (await reader.Trips.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task TripInProgress_NextToPlannedAndEndedTripsAndTheTripsOfOtherDrivers_IsAccepted()
    {
        // Arrange
        await SaveAsync(NewTripInProgress(driverId: Guid.NewGuid()));
        await SaveAsync(NewPlannedTrip());
        await SaveAsync(NewPlannedTrip());
        await SaveAsync(NewEndedTrip(number: 1));
        await SaveAsync(NewEndedTrip(number: 2));

        // Act
        await SaveAsync(NewTripInProgress());

        // Assert
        await using var reader = NewContext();
        (await reader.Trips.CountAsync(Ct)).ShouldBe(6);
    }

    [Fact]
    public async Task DeletedTrip_StaysInTheTableWithWhoAndWhen_AndNoQueryReturnsIt()
    {
        // Arrange
        var trip = NewPlannedTrip();
        var deletedAt = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
        var deletedBy = new UserId(Guid.NewGuid());
        trip.Delete(deletedAt, deletedBy).IsSuccess.ShouldBeTrue();

        // Act
        await SaveAsync(trip);

        // Assert
        await using var reader = NewContext();
        (await reader.Trips.CountAsync(Ct)).ShouldBe(0);
        (await reader.Trips.AnyAsync(t => t.Id == trip.Id, Ct)).ShouldBeFalse();

        var kept = await reader.Trips.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == trip.Id, Ct);
        kept.DeletedAt.ShouldBe(deletedAt);
        kept.DeletedBy.ShouldBe(deletedBy);
    }

    [Fact]
    public async Task DeletedTripInProgress_DoesNotCountAsTheDriversTripInProgress()
    {
        // Arrange
        var deleted = NewTripInProgress();
        deleted.Delete(AddedAt, new UserId(Guid.NewGuid())).IsSuccess.ShouldBeTrue();
        await SaveAsync(deleted);

        // Act
        await SaveAsync(NewTripInProgress());

        // Assert
        await using var reader = NewContext();
        (await reader.Trips.CountAsync(Ct)).ShouldBe(1);
        (await reader.Trips.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task EndedTrip_MarkedAsDeleted_IsRefusedByTheDatabase()
    {
        // Arrange
        var trip = NewEndedTrip();
        await SaveAsync(trip);

        // Act
        await using var db = NewContext();
        var exception = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"UPDATE operations.trips SET deleted_at = now(), deleted_by = {Guid.NewGuid()} WHERE id = {trip.Id.Value}",
            Ct));

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("chk_trips_deleted");
    }
}
