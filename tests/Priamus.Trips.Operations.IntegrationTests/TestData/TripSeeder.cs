using Microsoft.EntityFrameworkCore;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.IntegrationTests.TestData;

/// <summary>
/// Puts trips of a driver in the database of a test, for the reads that look at the driver's past trips.
/// A trip is priced by trip (start time only) unless an end is given, then by hour; only its customer, locations, vehicles, end and added moment vary (the trip day and start follow the added moment).
/// Every seeded trip is ended: one seeded without an end ends a minute after it starts.
/// </summary>
public sealed class TripSeeder
{
    private readonly string _connectionString;

    public TripSeeder(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Saves one trip of <paramref name="driverId"/> added at <paramref name="addedAt"/>; it is ended at
    /// the same moment unless <paramref name="endedAt"/> says otherwise.
    /// </summary>
    public async Task AddAsync(
        Guid driverId,
        Guid customerId,
        DateTime addedAt,
        Guid? pickupLocationId = null,
        Guid? deliveryLocationId = null,
        Guid? leadingVehicleId = null,
        Guid? trailerId = null,
        TimeOnly? end = null,
        DateTime? endedAt = null)
    {
        var pricing = new RegistryContracts.TripPricing(
            RegistryContracts.TripRateSource.Contract,
            null,
            null,
            null,
            DayPrice: 60m,
            NightPrice: 70m,
            NightCallFee: 5m,
            RegistryContracts.TripMinimumKind.None,
            MinimumAmount: null,
            MinimumThreshold: null,
            MinimumOveragePrice: null,
            NightBandStart: new TimeOnly(22, 0),
            NightBandEnd: new TimeOnly(6, 0),
            NightSurcharge: true,
            IsNight: false,
            Quantity: 1,
            UnitPrice: 60m,
            BaseAmount: 60m,
            AmountAfterMinimum: 60m,
            MinimumApplied: false,
            AppliedNightCallFee: null,
            Total: 60m);
        var priced = new RegistryContracts.PricedTrip(
            Guid.NewGuid(),
            end is null ? RegistryContracts.TripMeasureKind.StartOnly : RegistryContracts.TripMeasureKind.StartAndEnd,
            [],
            pricing);

        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(_connectionString).Options);
        var number = await db.NextTripNumberAsync(TestContext.Current.CancellationToken);

        var addedInItaly = new FixedClockStub(addedAt).NowInItaly();
        var trip = Build(
            TripId.New(),
            driverId,
            customerId,
            leadingVehicleId ?? Guid.NewGuid(),
            trailerId,
            pickupLocationId,
            deliveryLocationId,
            // The trip is done when it is added: day and start come from that moment in Italy.
            DateOnly.FromDateTime(addedInItaly),
            TimeOnly.FromDateTime(addedInItaly),
            end,
            null,
            null,
            priced,
            number,
            addedAt,
            new UserId(Guid.NewGuid()),
            endedAt);

        db.Trips.Add(trip);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Saves one trip in progress of <paramref name="driverId"/>, started at <paramref name="startedAt"/> and added at
    /// <paramref name="addedAt"/> (the start moment when not given).
    /// </summary>
    public async Task AddInProgressAsync(
        Guid driverId,
        Guid customerId,
        DateTime startedAt,
        Guid leadingVehicleId,
        Guid? trailerId = null,
        DateTime? addedAt = null)
    {
        var trip = BuildInProgress(
            TripId.New(),
            driverId,
            customerId,
            leadingVehicleId,
            trailerId,
            null,
            null,
            DateOnly.FromDateTime(startedAt),
            TimeOnly.FromDateTime(startedAt),
            addedAt ?? startedAt,
            new UserId(Guid.NewGuid()));

        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(_connectionString).Options);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Saves one planned trip of <paramref name="driverId"/>, added at <paramref name="addedAt"/>.</summary>
    public async Task AddPlannedAsync(
        Guid driverId,
        Guid customerId,
        DateTime addedAt,
        Guid? pickupLocationId = null,
        Guid? deliveryLocationId = null)
    {
        var trip = BuildPlanned(
            TripId.New(),
            driverId,
            customerId,
            Guid.NewGuid(),
            null,
            pickupLocationId,
            deliveryLocationId,
            addedAt,
            new UserId(Guid.NewGuid()));

        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(_connectionString).Options);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Saves <paramref name="trip"/> as it is, for a test that builds its own trip with <c>Build</c>.</summary>
    public async Task SaveAsync(Trip trip)
    {
        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(_connectionString).Options);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What the registry priced a trip with, for <c>Build</c>: <paramref name="measure"/>, the
    /// <paramref name="total"/> and <paramref name="unitPrice"/>, and whether the trip started in the night band
    /// of a contract with the night surcharge.
    /// </summary>
    public static RegistryContracts.PricedTrip Priced(
        RegistryContracts.TripMeasureKind measure,
        decimal total,
        decimal unitPrice = 60m,
        int? quantity = 1,
        bool isNight = false,
        bool nightSurcharge = true) =>
        new(
            Guid.NewGuid(),
            measure,
            [],
            new RegistryContracts.TripPricing(
                RegistryContracts.TripRateSource.Contract,
                null,
                null,
                null,
                DayPrice: 60m,
                NightPrice: 70m,
                NightCallFee: 5m,
                RegistryContracts.TripMinimumKind.None,
                MinimumAmount: null,
                MinimumThreshold: null,
                MinimumOveragePrice: null,
                NightBandStart: new TimeOnly(22, 0),
                NightBandEnd: new TimeOnly(6, 0),
                NightSurcharge: nightSurcharge,
                IsNight: isNight,
                Quantity: quantity,
                UnitPrice: unitPrice,
                BaseAmount: total,
                AmountAfterMinimum: total,
                MinimumApplied: false,
                AppliedNightCallFee: null,
                Total: total));

    /// <summary>The day in Italy of <paramref name="addedAt"/>, as the domain takes the day a trip was added.</summary>
    private static DateOnly AddedOnInItaly(DateTime addedAt) =>
        new FixedClockStub(addedAt).TodayInItaly();

    /// <summary>Builds a planned trip through <c>Add</c>: it has no day, no time and no number.</summary>
    public static Trip BuildPlanned(
        TripId id,
        Guid driverId,
        Guid customerId,
        Guid leadingVehicleId,
        Guid? trailerId,
        Guid? pickupLocationId,
        Guid? deliveryLocationId,
        DateTime addedAt,
        UserId addedBy) =>
        Trip.Add(
            id,
            driverId,
            customerId,
            leadingVehicleId,
            trailerId,
            pickupLocationId,
            deliveryLocationId,
            addedAt,
            AddedOnInItaly(addedAt),
            addedBy);

    /// <summary>Builds a trip in progress through <c>Add</c> and <c>StartAt</c>.</summary>
    public static Trip BuildInProgress(
        TripId id,
        Guid driverId,
        Guid customerId,
        Guid leadingVehicleId,
        Guid? trailerId,
        Guid? pickupLocationId,
        Guid? deliveryLocationId,
        DateOnly day,
        TimeOnly start,
        DateTime addedAt,
        UserId addedBy)
    {
        var trip = BuildPlanned(
            id, driverId, customerId, leadingVehicleId, trailerId, pickupLocationId, deliveryLocationId, addedAt, addedBy);
        trip.StartAt(day, start).IsSuccess.ShouldBeTrue();
        return trip;
    }

    /// <summary>
    /// Builds an ended trip through <c>Add</c>, <c>StartAt</c> and <c>EndAt</c>, with the given values. An
    /// <paramref name="end"/> of <c>null</c> ends the trip a minute after it starts.
    /// </summary>
    public static Trip Build(
        TripId id,
        Guid driverId,
        Guid customerId,
        Guid leadingVehicleId,
        Guid? trailerId,
        Guid? pickupLocationId,
        Guid? deliveryLocationId,
        DateOnly day,
        TimeOnly start,
        TimeOnly? end,
        int? kilograms,
        int? kilometres,
        RegistryContracts.PricedTrip priced,
        long number,
        DateTime addedAt,
        UserId addedBy,
        DateTime? endedAt = null)
    {
        var trip = BuildInProgress(
            id, driverId, customerId, leadingVehicleId, trailerId, pickupLocationId, deliveryLocationId,
            day, start, addedAt, addedBy);
        trip.EndAt(end ?? start.AddMinutes(1), kilograms, kilometres, priced, number, endedAt ?? addedAt)
            .IsSuccess.ShouldBeTrue();
        return trip;
    }
}
