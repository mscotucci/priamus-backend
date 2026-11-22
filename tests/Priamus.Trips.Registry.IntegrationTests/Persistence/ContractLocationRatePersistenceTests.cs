using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of the location rates of a contract against the per-test PostgreSQL database: the rows
/// of <c>registry.contract_location_rates</c>, the unique index on the location or pair, the foreign
/// keys and the audit rows.
/// </summary>
public sealed class ContractLocationRatePersistenceTests : RegistryIntegrationTestBase
{
    private const string UniqueIndex = "uq_contract_location_rates_locations";

    public ContractLocationRatePersistenceTests(PostgresFixture fixture) : base(fixture) { }

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

    private static Rate DayOnly(decimal dayPrice = 48m) => Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value;

    private static Rate NightWithTieredMinimum() =>
        Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value;

    private static Rate NightWithFixedMinimum() =>
        Rate.Create(40m, 55m, 8m, new MinimumRule.FixedMinimum(90m)).Value;

    private static Contract NewContract(
        CustomerId customerId,
        BillingLevel level,
        bool nightSurcharge,
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
            [],
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

    private async Task UpdateRatesAsync(Contract contract, params LocationRate[] locationRates)
    {
        await using var db = CreateDbContext();
        var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        loaded.Update(
            loaded.Validity, loaded.BillingLevel, loaded.PricingMode, loaded.RequiredDocuments,
            loaded.NightSurcharge, loaded.Rate, [], locationRates, new SystemClock(), FakeCurrentUser.ActorId)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    // A location contract with no rows, as one stored before rates were required: raw rows are then inserted.
    private async Task<Contract> SeedWithoutRatesAsync(CustomerId customerId, BillingLevel level)
    {
        await using var db = CreateDbContext();
        return await ContractTestSupport.SeedLocationContractWithoutRatesAsync(
            db, customerId, new DateOnly(2026, 4, 1), null, level);
    }

    private async Task<int> CountRowsAsync()
    {
        await using var read = CreateDbContext();
        return await read.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM registry.contract_location_rates")
            .SingleAsync(Ct);
    }

    private async Task<JsonDocument> AuditPayloadAsync(Contract contract, string eventType)
    {
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(a => a.SubjectId == contract.Id.Value && a.EventType == eventType, Ct);
        return JsonDocument.Parse(row.Payload);
    }

    private static Task InsertRowAsync(RegistryDbContext db, Guid contractId, Guid? pickup, Guid? delivery) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO registry.contract_location_rates
                (contract_id, pickup_location_id, delivery_location_id, day_price, minimum_rule)
            VALUES ({contractId}, {pickup}, {delivery}, 48, 'None')
            """,
            Ct);

    [Fact]
    public async Task PickupLocationRates_RoundTripThroughTheirOwnTable()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedLocationAsync("Cava Nord");
        var second = await SeedLocationAsync("Cava Sud");
        var withNight = new LocationRate(first.Id, null, NightWithTieredMinimum());
        var withFixed = new LocationRate(second.Id, null, NightWithFixedMinimum());
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.PerPickupLocation, true, withNight, withFixed));
        var otherCustomer = await SeedCustomerAsync(VatNumberB);
        var withoutRates = await SaveAsync(NewContract(otherCustomer.Id, BillingLevel.FlatRate, false));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        var loadedWithout = await read.Contracts.SingleAsync(c => c.Id == withoutRates.Id, Ct);

        // Assert
        loaded.LocationRates.ShouldBe([withNight, withFixed], ignoreOrder: true);
        loadedWithout.LocationRates.ShouldBeEmpty();
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task DeliveryLocationRates_RoundTripThroughTheirOwnTable()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedLocationAsync("Discarica Est");
        var second = await SeedLocationAsync("Discarica Ovest");
        var dayOnly = new LocationRate(null, first.Id, DayOnly());
        var tiered = new LocationRate(
            null, second.Id, Rate.Create(60m, null, null, new MinimumRule.TieredOverage(300m, 4m, 55m)).Value);
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.PerDeliveryLocation, false, dayOnly, tiered));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        loaded.LocationRates.ShouldBe([dayOnly, tiered], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task PairRates_RoundTripAndKeepBothDirectionsAndTheSameLocationOnBothSides()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var ab = new LocationRate(a.Id, b.Id, NightWithTieredMinimum());
        var ba = new LocationRate(b.Id, a.Id, NightWithFixedMinimum());
        var aa = new LocationRate(a.Id, a.Id, Rate.Create(30m, 31m, 2m, new MinimumRule.None()).Value);
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.PerLocationPair, true, ab, ba, aa));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        loaded.LocationRates.ShouldBe([ab, ba, aa], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task LocationRates_ReplacedOnUpdateKeepingOneLocation_ReplaceTheWholeListWithTheUniqueIndex()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedLocationAsync("Cava Nord");
        var removed = await SeedLocationAsync("Cava Sud");
        var added = await SeedLocationAsync("Cava Ovest");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false,
            new LocationRate(kept.Id, null, DayOnly()), new LocationRate(removed.Id, null, DayOnly())));
        var repriced = new LocationRate(kept.Id, null, Rate.Create(55m, null, null, new MinimumRule.FixedMinimum(120m)).Value);
        var addedRate = new LocationRate(added.Id, null, DayOnly(61m));

        // Act
        await UpdateRatesAsync(contract, repriced, addedRate);

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        reloaded.LocationRates.ShouldBe([repriced, addedRate], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task PairRates_ReplacedOnUpdateKeepingOnePair_ReplaceTheWholeListWithTheUniqueIndex()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerLocationPair, false,
            new LocationRate(a.Id, b.Id, DayOnly()), new LocationRate(b.Id, a.Id, DayOnly())));
        var repriced = new LocationRate(a.Id, b.Id, DayOnly(77m));
        var added = new LocationRate(b.Id, b.Id, DayOnly(33m));

        // Act
        await UpdateRatesAsync(contract, repriced, added);

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        reloaded.LocationRates.ShouldBe([repriced, added], ignoreOrder: true);
        (await CountRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task LocationRates_DroppedWhenTheContractBecomesFlatRate_LeaveNoRows()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false, new LocationRate(location.Id, null, DayOnly())));

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, BillingLevel.FlatRate, loaded.PricingMode, loaded.RequiredDocuments,
                loaded.NightSurcharge, DayOnly(), [], [], new SystemClock(), FakeCurrentUser.ActorId)
                .IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct)).LocationRates.ShouldBeEmpty();
        (await CountRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeletingAContract_RemovesItsLocationRatesAndNoOthers()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var doomed = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false, new LocationRate(location.Id, null, DayOnly())));
        var otherCustomer = await SeedCustomerAsync(VatNumberB);
        var other = await SaveAsync(NewContract(
            otherCustomer.Id, BillingLevel.PerPickupLocation, false, new LocationRate(location.Id, null, DayOnly())));

        // Act
        await using (var db = CreateDbContext())
        {
            db.Contracts.Remove(await db.Contracts.SingleAsync(c => c.Id == doomed.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == other.Id, Ct)).LocationRates.Count.ShouldBe(1);
        (await CountRowsAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UniqueIndex_RefusesTheSameLocationTwiceInOneContract(bool onDelivery)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var contract = await SeedWithoutRatesAsync(
            customer.Id, onDelivery ? BillingLevel.PerDeliveryLocation : BillingLevel.PerPickupLocation);
        await using var db = CreateDbContext();
        var pickup = onDelivery ? (Guid?)null : location.Id.Value;
        var delivery = onDelivery ? location.Id.Value : (Guid?)null;
        await InsertRowAsync(db, contract.Id.Value, pickup, delivery);

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRowAsync(db, contract.Id.Value, pickup, delivery));

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.ShouldBe(UniqueIndex);
    }

    [Fact]
    public async Task UniqueIndex_RefusesTheSamePairTwiceInOneContractButAcceptsItsReverseAndAnotherContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");
        var contract = await SeedWithoutRatesAsync(customer.Id, BillingLevel.PerLocationPair);
        var otherCustomer = await SeedCustomerAsync(VatNumberB);
        var other = await SeedWithoutRatesAsync(otherCustomer.Id, BillingLevel.PerLocationPair);
        await using var db = CreateDbContext();
        await InsertRowAsync(db, contract.Id.Value, a.Id.Value, b.Id.Value);

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(
            () => InsertRowAsync(db, contract.Id.Value, a.Id.Value, b.Id.Value));
        await InsertRowAsync(db, contract.Id.Value, b.Id.Value, a.Id.Value);
        await InsertRowAsync(db, other.Id.Value, a.Id.Value, b.Id.Value);

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.ShouldBe(UniqueIndex);
        (await CountRowsAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task LocationRate_OnAnUnknownLocationId_IsRefusedByTheForeignKey()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var db = CreateDbContext();
        db.Contracts.Add(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, false, new LocationRate(LocationId.New(), null, DayOnly())));

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        // Assert
        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState
            .ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task DeactivatingALocationWithARate_IsNotRefusedAndTheRateStaysOnTheContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Cava Nord");
        var rate = new LocationRate(location.Id, null, DayOnly());
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.PerPickupLocation, false, rate));

        // Act
        await using (var db = CreateDbContext())
        {
            var loadedLocation = await db.Locations.SingleAsync(l => l.Id == location.Id, Ct);
            loadedLocation.Deactivate(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct)).LocationRates.ShouldBe([rate]);
    }

    [Fact]
    public async Task CreatedContractWithPairRates_WritesThemInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationAsync("Cava Nord");
        var b = await SeedLocationAsync("Discarica Est");

        // Act
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerLocationPair, true,
            new LocationRate(a.Id, b.Id, NightWithTieredMinimum()),
            new LocationRate(b.Id, a.Id, Rate.Create(48m, 58m, 4m, new MinimumRule.None()).Value)));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        var items = payload.RootElement.GetProperty("locationRates").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        var tiered = items.Single(i => i.GetProperty("pickupLocationId").GetGuid() == a.Id.Value);
        tiered.GetProperty("deliveryLocationId").GetGuid().ShouldBe(b.Id.Value);
        tiered.GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
        tiered.GetProperty("nightPrice").GetDecimal().ShouldBe(70.25m);
        tiered.GetProperty("nightCallFee").GetDecimal().ShouldBe(12m);
        tiered.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        tiered.GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        tiered.GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        tiered.GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
        var plain = items.Single(i => i.GetProperty("pickupLocationId").GetGuid() == b.Id.Value);
        plain.GetProperty("deliveryLocationId").GetGuid().ShouldBe(a.Id.Value);
        plain.GetProperty("dayPrice").GetDecimal().ShouldBe(48m);
        plain.GetProperty("minimumRule").GetString().ShouldBe("None");
        plain.TryGetProperty("minimumAmount", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task CreatedContractWithDeliveryRates_OmitsThePickupFromTheAuditItems()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationAsync("Discarica Est");

        // Act
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerDeliveryLocation, false, new LocationRate(null, location.Id, DayOnly())));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        var item = payload.RootElement.GetProperty("locationRates").EnumerateArray().Single();
        item.GetProperty("deliveryLocationId").GetGuid().ShouldBe(location.Id.Value);
        item.TryGetProperty("pickupLocationId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdatedContract_WritesTheLocationRatesAfterTheChangeInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedLocationAsync("Cava Nord");
        var second = await SeedLocationAsync("Cava Sud");
        var contract = await SaveAsync(NewContract(
            customer.Id, BillingLevel.PerPickupLocation, true, new LocationRate(first.Id, null, NightWithFixedMinimum())));

        // Act
        await UpdateRatesAsync(contract, new LocationRate(second.Id, null, NightWithTieredMinimum()));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractUpdated);
        var item = payload.RootElement.GetProperty("locationRates").EnumerateArray().Single();
        item.GetProperty("pickupLocationId").GetGuid().ShouldBe(second.Id.Value);
        item.GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
    }

    [Fact]
    public async Task FlatRateContract_OmitsTheLocationRatesFromTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();

        // Act
        var contract = await SaveAsync(NewContract(customer.Id, BillingLevel.FlatRate, false));

        // Assert
        using var payload = await AuditPayloadAsync(contract, AuditEventTypes.ContractCreated);
        payload.RootElement.TryGetProperty("locationRates", out _).ShouldBeFalse();
    }
}
