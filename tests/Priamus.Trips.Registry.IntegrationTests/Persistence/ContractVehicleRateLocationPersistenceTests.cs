using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of the vehicle rates of a contract against the per-test PostgreSQL database: the rows
/// of <c>registry.contract_vehicle_rates</c> with their locations, the unique index on the vehicle and
/// the location or pair, the foreign keys and the audit rows.
/// </summary>
public sealed class ContractVehicleRateLocationPersistenceTests : RegistryIntegrationTestBase
{
    private const string UniqueIndex = "uq_contract_vehicle_rates_vehicle_locations";

    public ContractVehicleRateLocationPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null);
    }

    private async Task<Location> SeedLocationAsync(string name)
    {
        await using var db = CreateDbContext();
        return await LocationTestSupport.SeedLocationAsync(db, name, "Pescara");
    }

    private async Task<Vehicle> SeedVehicleAsync(string licensePlate)
    {
        await using var db = CreateDbContext();
        return await VehicleTestSupport.SeedVehicleAsync(db, licensePlate);
    }

    private static Rate DayOnly(decimal dayPrice = 48m) => Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value;

    private static Rate NightWithTieredMinimum() =>
        Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value;

    private static Rate NightWithFixedMinimum() =>
        Rate.Create(40m, 55m, 8m, new MinimumRule.FixedMinimum(90m)).Value;

    // The key of the level: the locations a location rate or a vehicle rate names.
    private static (LocationId? Pickup, LocationId? Delivery) Key(BillingLevel level, Location first, Location second) =>
        level switch
        {
            BillingLevel.PerPickupLocation => (first.Id, null),
            BillingLevel.PerDeliveryLocation => (null, first.Id),
            _ => (first.Id, second.Id),
        };

    private static Contract NewContract(
        CustomerId customerId,
        BillingLevel level,
        bool nightSurcharge,
        IReadOnlyList<VehicleRate> vehicleRates,
        params LocationRate[] locationRates) =>
        Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(new DateOnly(2026, 4, 1), null).Value,
            level,
            PricingMode.ByHour,
            [],
            nightSurcharge,
            rate: level == BillingLevel.FlatRate ? DayOnly() : null,
            vehicleRates,
            locationRates,
            new SystemClock(),
            FakeCurrentUser.ActorId).Value;

    private async Task<Contract> SaveAsync(Contract contract)
    {
        await using var db = CreateDbContext();
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(Ct);
        return contract;
    }

    private async Task UpdateVehicleRatesAsync(Contract contract, params VehicleRate[] vehicleRates)
    {
        await using var db = CreateDbContext();
        var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        loaded.Update(
            loaded.Validity, loaded.BillingLevel, loaded.PricingMode, loaded.RequiredDocuments,
            loaded.NightSurcharge, loaded.Rate, vehicleRates, loaded.LocationRates, new SystemClock(), FakeCurrentUser.ActorId)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task<int> CountRowsAsync()
    {
        await using var read = CreateDbContext();
        return await read.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM registry.contract_vehicle_rates")
            .SingleAsync(Ct);
    }

    private async Task<JsonDocument> AuditPayloadAsync(Contract contract, string eventType)
    {
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(a => a.SubjectId == contract.Id.Value && a.EventType == eventType, Ct);
        return JsonDocument.Parse(row.Payload);
    }

    private static Task InsertRowAsync(RegistryDbContext db, Guid contractId, Guid vehicleId, Guid? pickup, Guid? delivery) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO registry.contract_vehicle_rates
                (contract_id, vehicle_id, pickup_location_id, delivery_location_id, day_price, minimum_rule)
            VALUES ({contractId}, {vehicleId}, {pickup}, {delivery}, 48, 'None')
            """,
            Ct);

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public async Task VehicleRates_OnALocationLevel_RoundTripWithSeveralRowsForOneVehicleAndOneRateOnSeveralLocations(BillingLevel level)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var c = await SeedLocationAsync("Cava Sud");
        var d = await SeedLocationAsync("Discarica Ovest");
        var firstKey = Key(level, a, b);
        var secondKey = Key(level, c, d);
        var tractor = await SeedVehicleAsync("AB456CD");
        var other = await SeedVehicleAsync("EF789GH");
        var onFirst = new VehicleRate(tractor.Id, firstKey.Pickup, firstKey.Delivery, NightWithTieredMinimum());
        var onSecond = new VehicleRate(tractor.Id, secondKey.Pickup, secondKey.Delivery, NightWithFixedMinimum());
        var otherOnFirst = new VehicleRate(other.Id, firstKey.Pickup, firstKey.Delivery, NightWithFixedMinimum());
        var otherOnSecond = new VehicleRate(other.Id, secondKey.Pickup, secondKey.Delivery, NightWithFixedMinimum());
        var contract = await SaveAsync(NewContract(
            customer.Id, level, true,
            [onFirst, onSecond, otherOnFirst, otherOnSecond],
            new LocationRate(firstKey.Pickup, firstKey.Delivery, NightWithTieredMinimum()),
            new LocationRate(secondKey.Pickup, secondKey.Delivery, NightWithTieredMinimum())));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(x => x.Id == contract.Id, Ct);

        // Assert
        loaded.VehicleRates.ShouldBe([onFirst, onSecond, otherOnFirst, otherOnSecond], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(4);
    }

    [Fact]
    public async Task PairVehicleRates_KeepBothDirectionsAndTheSameLocationOnBothSides()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var tractor = await SeedVehicleAsync("AB456CD");
        var ab = new VehicleRate(tractor.Id, a.Id, b.Id, DayOnly(50m));
        var ba = new VehicleRate(tractor.Id, b.Id, a.Id, DayOnly(51m));
        var aa = new VehicleRate(tractor.Id, a.Id, a.Id, DayOnly(52m));
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerLocationPair, false, [ab, ba, aa],
            new LocationRate(a.Id, b.Id, DayOnly()), new LocationRate(b.Id, a.Id, DayOnly()), new LocationRate(a.Id, a.Id, DayOnly())));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(x => x.Id == contract.Id, Ct);

        // Assert
        loaded.VehicleRates.ShouldBe([ab, ba, aa], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task FlatRateVehicleRates_AreStoredOnePerVehicleWithNullLocationColumns()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleAsync("AB456CD");
        var second = await SeedVehicleAsync("EF789GH");
        var withNight = new VehicleRate(first.Id, null, null, DayOnly(50m));
        var dayOnly = new VehicleRate(second.Id, null, null, DayOnly(55m));
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.FlatRate, false, [withNight, dayOnly]));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(x => x.Id == contract.Id, Ct);
        var rowsWithLocations = await read.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM registry.contract_vehicle_rates WHERE pickup_location_id IS NOT NULL OR delivery_location_id IS NOT NULL")
            .SingleAsync(Ct);

        // Assert
        loaded.VehicleRates.ShouldBe([withNight, dayOnly], ignoreOrder: true);
        loaded.VehicleRates.ShouldAllBe(r => r.PickupLocationId == null && r.DeliveryLocationId == null);
        rowsWithLocations.ShouldBe(0);
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public async Task VehicleRates_ReplacedOnUpdateKeepingOneVehicleAndLocation_ReplaceTheWholeListWithTheUniqueIndex(BillingLevel level)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var c = await SeedLocationAsync("Cava Sud");
        var d = await SeedLocationAsync("Discarica Ovest");
        var firstKey = Key(level, a, b);
        var secondKey = Key(level, c, d);
        var tractor = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContract(
            customer.Id, level, false,
            [
                new VehicleRate(tractor.Id, firstKey.Pickup, firstKey.Delivery, DayOnly()),
                new VehicleRate(tractor.Id, secondKey.Pickup, secondKey.Delivery, DayOnly()),
            ],
            new LocationRate(firstKey.Pickup, firstKey.Delivery, DayOnly()),
            new LocationRate(secondKey.Pickup, secondKey.Delivery, DayOnly())));
        var repriced = new VehicleRate(tractor.Id, firstKey.Pickup, firstKey.Delivery, Rate.Create(55m, null, null, new MinimumRule.FixedMinimum(120m)).Value);
        var other = await SeedVehicleAsync("EF789GH");
        var added = new VehicleRate(other.Id, firstKey.Pickup, firstKey.Delivery, DayOnly(61m));

        // Act
        await UpdateVehicleRatesAsync(contract, repriced, added);

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(x => x.Id == contract.Id, Ct);
        reloaded.VehicleRates.ShouldBe([repriced, added], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData(BillingLevel.FlatRate)]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public async Task UniqueIndex_RefusesTheSameVehicleTwiceOnOneLocationOrPairOrOnAFlatRateContract(BillingLevel level)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var key = level == BillingLevel.FlatRate ? (Pickup: (LocationId?)null, Delivery: (LocationId?)null) : Key(level, a, b);
        var tractor = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContract(
            customer.Id,
            level,
            false,
            [],
            level == BillingLevel.FlatRate ? [] : [new LocationRate(key.Pickup, key.Delivery, DayOnly())]));
        await using var db = CreateDbContext();
        await InsertRowAsync(db, contract.Id.Value, tractor.Id.Value, key.Pickup?.Value, key.Delivery?.Value);

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(
            () => InsertRowAsync(db, contract.Id.Value, tractor.Id.Value, key.Pickup?.Value, key.Delivery?.Value));

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.ShouldBe(UniqueIndex);
    }

    [Fact]
    public async Task UniqueIndex_AcceptsTheSameVehicleOnAnotherLocationTheReversePairAndAnotherVehicleOrContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var tractor = await SeedVehicleAsync("AB456CD");
        var other = await SeedVehicleAsync("EF789GH");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerLocationPair, false, [],
            new LocationRate(a.Id, b.Id, DayOnly()), new LocationRate(b.Id, a.Id, DayOnly())));
        var otherCustomer = await SeedCustomerAsync(VatNumberB);
        var otherContract = await SaveAsync(NewContract(
            otherCustomer.Id, BillingLevel.PerLocationPair, false, [], new LocationRate(a.Id, b.Id, DayOnly())));
        await using var db = CreateDbContext();
        await InsertRowAsync(db, contract.Id.Value, tractor.Id.Value, a.Id.Value, b.Id.Value);

        // Act
        await InsertRowAsync(db, contract.Id.Value, tractor.Id.Value, b.Id.Value, a.Id.Value);
        await InsertRowAsync(db, contract.Id.Value, other.Id.Value, a.Id.Value, b.Id.Value);
        await InsertRowAsync(db, otherContract.Id.Value, tractor.Id.Value, a.Id.Value, b.Id.Value);

        // Assert
        (await CountRowsAsync()).ShouldBe(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VehicleRate_OnAnUnknownLocationId_IsRefusedByTheForeignKey(bool onDelivery)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var tractor = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false, [], new LocationRate(location.Id, null, DayOnly())));
        await using var db = CreateDbContext();

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRowAsync(
            db, contract.Id.Value, tractor.Id.Value, onDelivery ? null : Guid.NewGuid(), onDelivery ? Guid.NewGuid() : null));

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task DeactivatingALocationWithAVehicleRate_IsNotRefusedAndTheVehicleRateStaysOnTheContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var tractor = await SeedVehicleAsync("AB456CD");
        var vehicleRate = new VehicleRate(tractor.Id, location.Id, null, DayOnly(55m));
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false, [vehicleRate], new LocationRate(location.Id, null, DayOnly())));

        // Act
        await using (var db = CreateDbContext())
        {
            var loadedLocation = await db.Locations.SingleAsync(l => l.Id == location.Id, Ct);
            loadedLocation.Deactivate(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(x => x.Id == contract.Id, Ct)).VehicleRates.ShouldBe([vehicleRate]);
    }

    [Fact]
    public async Task CreatedContractWithPairVehicleRates_WritesTheirLocationsInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var tractor = await SeedVehicleAsync("AB456CD");

        // Act
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerLocationPair, true,
            [new VehicleRate(tractor.Id, a.Id, b.Id, NightWithTieredMinimum())],
            new LocationRate(a.Id, b.Id, NightWithFixedMinimum())));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        var item = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().Single();
        item.GetProperty("vehicleId").GetGuid().ShouldBe(tractor.Id.Value);
        item.GetProperty("pickupLocationId").GetGuid().ShouldBe(a.Id.Value);
        item.GetProperty("deliveryLocationId").GetGuid().ShouldBe(b.Id.Value);
        item.GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
        item.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
    }

    [Fact]
    public async Task CreatedContractWithPickupVehicleRates_OmitsTheDeliveryFromTheAuditItems()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var tractor = await SeedVehicleAsync("AB456CD");

        // Act
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false,
            [new VehicleRate(tractor.Id, location.Id, null, DayOnly())],
            new LocationRate(location.Id, null, DayOnly())));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        var item = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().Single();
        item.GetProperty("pickupLocationId").GetGuid().ShouldBe(location.Id.Value);
        item.TryGetProperty("deliveryLocationId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdatedContract_WritesTheVehicleRateLocationsAfterTheChangeInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedLocationAsync("Cava Nord");
        var second = await SeedLocationAsync("Cava Sud");
        var tractor = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false,
            [new VehicleRate(tractor.Id, first.Id, null, DayOnly())],
            new LocationRate(first.Id, null, DayOnly()), new LocationRate(second.Id, null, DayOnly())));

        // Act
        await UpdateVehicleRatesAsync(contract, new VehicleRate(tractor.Id, second.Id, null, DayOnly(61m)));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractUpdated);
        var item = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().Single();
        item.GetProperty("pickupLocationId").GetGuid().ShouldBe(second.Id.Value);
        item.GetProperty("dayPrice").GetDecimal().ShouldBe(61m);
    }

    [Fact]
    public async Task FlatRateVehicleRates_OmitBothLocationsFromTheAuditItems()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync("AB456CD");

        // Act
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.FlatRate, false, [new VehicleRate(tractor.Id, null, null, DayOnly(55m))]));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        var item = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().Single();
        item.GetProperty("vehicleId").GetGuid().ShouldBe(tractor.Id.Value);
        item.TryGetProperty("pickupLocationId", out _).ShouldBeFalse();
        item.TryGetProperty("deliveryLocationId", out _).ShouldBeFalse();
    }
}
