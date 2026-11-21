using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of contracts against the per-test PostgreSQL database: the mapping, the exclusion
/// constraint that forbids overlapping validities of one customer, and the audit rows.
/// </summary>
public sealed class ContractPersistenceTests : RegistryIntegrationTestBase
{
    private const string ConstraintName = "ex_contracts_customer_validity";

    public ContractPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static Contract NewContract(
        CustomerId customerId,
        DateOnly validFrom,
        DateOnly? validTo,
        RequiredDocument[]? documents = null) =>
        Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(validFrom, validTo).Value,
            BillingLevel.PerLocationPair,
            PricingMode.ByTrip,
            documents ?? [RequiredDocument.DailySummary, RequiredDocument.DeliveryNote],
            nightSurcharge: true,
            rate: null,
            [],
            new SystemClock(),
            FakeCurrentUser.ActorId).Value;

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null);
    }

    private async Task<Contract> SeedContractAsync(CustomerId customerId, DateOnly validFrom, DateOnly? validTo)
    {
        await using var db = CreateDbContext();
        var contract = NewContract(customerId, validFrom, validTo);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(Ct);
        return contract;
    }

    // The constraint is deferred: Npgsql raises the violation at commit, and EF Core lets the
    // PostgresException through unwrapped (no DbUpdateException).
    private static void ShouldBeOverlapViolation(PostgresException exception) =>
        exception.ShouldSatisfyAllConditions(
            pg => pg.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation),
            pg => pg.ConstraintName.ShouldBe(ConstraintName));

    [Fact]
    public async Task Contract_RoundTripsThroughTheDatabase()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var open = NewContract(customer.Id, D(2027, 1, 1), null,
            [RequiredDocument.WasteForm, RequiredDocument.DeliveryNote, RequiredDocument.WasteForm]);
        var closed = NewContract(customer.Id, D(2026, 1, 1), D(2026, 12, 31), []);
        await using (var db = CreateDbContext())
        {
            db.Contracts.AddRange(open, closed);
            await db.SaveChangesAsync(Ct);
        }

        // Act
        await using var read = CreateDbContext();
        var loadedOpen = await read.Contracts.SingleAsync(c => c.Id == open.Id, Ct);
        var loadedClosed = await read.Contracts.SingleAsync(c => c.Id == closed.Id, Ct);

        // Assert
        loadedOpen.CustomerId.ShouldBe(customer.Id);
        loadedOpen.Validity.ValidFrom.ShouldBe(D(2027, 1, 1));
        loadedOpen.Validity.ValidTo.ShouldBeNull();
        loadedOpen.BillingLevel.ShouldBe(BillingLevel.PerLocationPair);
        loadedOpen.PricingMode.ShouldBe(PricingMode.ByTrip);
        loadedOpen.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.WasteForm]);
        loadedOpen.NightSurcharge.ShouldBeTrue();
        loadedOpen.CreatedBy.ShouldBe(FakeCurrentUser.ActorId);
        loadedClosed.Validity.ValidTo.ShouldBe(D(2026, 12, 31));
        loadedClosed.RequiredDocuments.ShouldBeEmpty();
    }

    [Fact]
    public async Task RequiredDocuments_ChangedInPlace_AreDetectedByContentAndSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedContractAsync(customer.Id, D(2026, 1, 1), null);

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, loaded.BillingLevel, loaded.PricingMode,
                [RequiredDocument.DailySummary, RequiredDocument.WasteForm, RequiredDocument.DeliveryNote],
                loaded.NightSurcharge, loaded.Rate,
                loaded.VehicleRates, new SystemClock(), FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        reloaded.RequiredDocuments.ShouldBe(
            [RequiredDocument.DeliveryNote, RequiredDocument.WasteForm, RequiredDocument.DailySummary]);
        reloaded.Version.ShouldBe(1);
    }

    public static TheoryData<string, int, int, int, int?, int?, int?> OverlappingValidities => new()
    {
        { "shares the last day", 2026, 12, 31, 2027, 6, 30 },
        { "shares the first day", 2025, 1, 1, 2026, 1, 1 },
        { "contained", 2026, 3, 1, 2026, 3, 31 },
        { "open, starting inside", 2026, 6, 1, null, null, null },
        { "same single day as the first", 2026, 1, 1, 2026, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(OverlappingValidities))]
    public async Task OverlappingValidityOfTheSameCustomer_IsRefusedByTheDatabase(
        string description, int fromYear, int fromMonth, int fromDay, int? toYear, int? toMonth, int? toDay)
    {
        // Arrange
        _ = description;
        var customer = await SeedCustomerAsync();
        await SeedContractAsync(customer.Id, D(2026, 1, 1), D(2026, 12, 31));
        var validTo = toYear is null ? (DateOnly?)null : D(toYear.Value, toMonth!.Value, toDay!.Value);
        await using var db = CreateDbContext();
        db.Contracts.Add(NewContract(customer.Id, D(fromYear, fromMonth, fromDay), validTo));

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(() => db.SaveChangesAsync(Ct));

        // Assert
        ShouldBeOverlapViolation(exception);
    }

    [Fact]
    public async Task ValidityAfterAnOpenContract_IsRefusedByTheDatabase()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await SeedContractAsync(customer.Id, D(2026, 1, 1), null);
        await using var db = CreateDbContext();
        db.Contracts.Add(NewContract(customer.Id, D(2030, 1, 1), D(2030, 12, 31)));

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(() => db.SaveChangesAsync(Ct));

        // Assert
        ShouldBeOverlapViolation(exception);
    }

    [Fact]
    public async Task AdjacentValiditiesAndAnotherCustomersValidity_AreAccepted()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        await SeedContractAsync(customer.Id, D(2026, 1, 1), D(2026, 12, 31));

        // Act
        await SeedContractAsync(customer.Id, D(2027, 1, 1), null);
        await SeedContractAsync(other.Id, D(2026, 6, 1), D(2026, 6, 30));

        // Assert
        await using var db = CreateDbContext();
        (await db.Contracts.CountAsync(c => c.CustomerId == customer.Id, Ct)).ShouldBe(2);
        (await db.Contracts.CountAsync(c => c.CustomerId == other.Id, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task ClosingTheOpenContractAndInsertingTheNextInOneSave_Succeeds()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var current = await SeedContractAsync(customer.Id, D(2026, 1, 1), null);
        await using var db = CreateDbContext();
        var open = await db.Contracts.SingleAsync(c => c.Id == current.Id, Ct);
        var next = NewContract(customer.Id, D(2026, 7, 1), null);
        db.Contracts.Add(next);
        open.CloseOn(D(2026, 6, 30), new SystemClock(), FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();

        // Act
        await db.SaveChangesAsync(Ct);

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == current.Id, Ct)).Validity.ValidTo.ShouldBe(D(2026, 6, 30));
        (await read.Contracts.SingleAsync(c => c.Id == next.Id, Ct)).Validity.ValidTo.ShouldBeNull();
    }

    [Fact]
    public async Task CreatedContract_WritesAnAuditRowWithTheFullState()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = NewContract(customer.Id, D(2026, 4, 1), null,
            [RequiredDocument.DailySummary, RequiredDocument.DeliveryNote]);

        // Act
        await using (var db = CreateDbContext())
        {
            db.Contracts.Add(contract);
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(a => a.SubjectId == contract.Id.Value, Ct);
        row.EventType.ShouldBe(AuditEventTypes.ContractCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;
        root.GetProperty("contractId").GetGuid().ShouldBe(contract.Id.Value);
        root.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        root.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        root.TryGetProperty("validTo", out _).ShouldBeFalse();
        root.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        root.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        root.GetProperty("requiredDocuments").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["DeliveryNote", "DailySummary"]);
        root.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task UpdatedContract_WritesAnAuditRowWithTheFullStateAfterTheChange()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedContractAsync(customer.Id, D(2026, 4, 1), null);

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                Validity.Create(D(2026, 4, 1), D(2026, 9, 30)).Value, BillingLevel.FlatRate, PricingMode.ByHour,
                [], nightSurcharge: false, Rate.Create(45m, null, null, new MinimumRule.None()).Value,
                [], new SystemClock(), FakeCurrentUser.ActorId)
                .IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(
            a => a.SubjectId == contract.Id.Value && a.EventType == AuditEventTypes.ContractUpdated, Ct);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;
        root.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        root.GetProperty("validTo").GetString().ShouldBe("2026-09-30");
        root.GetProperty("billingLevel").GetString().ShouldBe("FlatRate");
        root.GetProperty("pricingMode").GetString().ShouldBe("ByHour");
        root.GetProperty("requiredDocuments").GetArrayLength().ShouldBe(0);
        root.GetProperty("nightSurcharge").GetBoolean().ShouldBeFalse();
        (await read.AuditLog.CountAsync(a => a.SubjectId == contract.Id.Value, Ct)).ShouldBe(2);
    }

    private static Contract NewFlatRateContract(CustomerId customerId, bool nightSurcharge, Rate? rate) =>
        Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(D(2026, 4, 1), null).Value,
            BillingLevel.FlatRate,
            PricingMode.ByHour,
            [],
            nightSurcharge,
            rate,
            [],
            new SystemClock(),
            FakeCurrentUser.ActorId).Value;

    private async Task<Contract> SaveAsync(Contract contract)
    {
        await using var db = CreateDbContext();
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(Ct);
        return contract;
    }

    public static TheoryData<string, bool, decimal?, decimal?, MinimumRule> RatesOfEveryKind => new()
    {
        { "no minimum", false, null, null, new MinimumRule.None() },
        { "fixed minimum with night values", true, 60.5m, 10m, new MinimumRule.FixedMinimum(120m) },
        { "tiered overage", false, null, null, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m) },
    };

    [Theory]
    [MemberData(nameof(RatesOfEveryKind))]
    public async Task Rate_RoundTripsOnTheContractRow(
        string description, bool nightSurcharge, decimal? nightPrice, decimal? nightCallFee, MinimumRule minimum)
    {
        // Arrange
        description.ShouldNotBeNullOrEmpty();
        var customer = await SeedCustomerAsync();
        var rate = Rate.Create(45.25m, nightPrice, nightCallFee, minimum).Value;
        var contract = await SaveAsync(NewFlatRateContract(customer.Id, nightSurcharge, rate));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        loaded.Rate.ShouldBe(rate);
    }

    [Fact]
    public async Task ContractStoredWithoutARate_LoadsWithNoRate()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var seed = CreateDbContext())
        {
            contract = await SeedFlatRateWithoutRateAsync(seed, customer.Id, D(2026, 4, 1), null);
        }


        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        loaded.Rate.ShouldBeNull();
    }

    [Fact]
    public async Task ContractStoredWithNightPriceBelowDayPrice_StillLoads()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SaveAsync(NewFlatRateContract(
            customer.Id, nightSurcharge: true, Rate.Create(45m, 55m, 5m, new MinimumRule.None()).Value));
        await using (var seed = CreateDbContext())
        {
            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE registry.contracts SET night_price = 40 WHERE id = {contract.Id.Value}", Ct);
        }

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        loaded.Rate.ShouldNotBeNull().NightPrice.ShouldBe(40m);
    }

    [Fact]
    public async Task VehicleRateStoredWithNightPriceBelowDayPrice_StillLoads()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContractWithVehicleRates(customer.Id, RateWithoutMinimum(vehicle)));
        await using (var seed = CreateDbContext())
        {
            await seed.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE registry.contract_vehicle_rates SET night_price = 40 WHERE contract_id = {contract.Id.Value}", Ct);
        }

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);

        // Assert
        var vehicleRate = loaded.VehicleRates.ShouldHaveSingleItem();
        vehicleRate.VehicleId.ShouldBe(vehicle.Id);
        vehicleRate.Rate.DayPrice.ShouldBe(48m);
        vehicleRate.Rate.NightPrice.ShouldBe(40m);
    }

    [Fact]
    public async Task Rate_AddedToALoadedContract_IsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var seed = CreateDbContext())
        {
            contract = await SeedFlatRateWithoutRateAsync(seed, customer.Id, D(2026, 4, 1), null);
        }

        var rate = Rate.Create(30m, null, null, new MinimumRule.FixedMinimum(90m)).Value;

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, loaded.BillingLevel, loaded.PricingMode, [], nightSurcharge: false, rate,
                [], new SystemClock(), FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct)).Rate.ShouldBe(rate);
    }

    [Fact]
    public async Task CreatedContractWithARate_WritesTheRateAndTheMinimumInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var rate = Rate.Create(45.25m, 60.5m, 10m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value;

        // Act
        var contract = await SaveAsync(NewFlatRateContract(customer.Id, nightSurcharge: true, rate));

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(a => a.SubjectId == contract.Id.Value, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;
        root.GetProperty("dayPrice").GetDecimal().ShouldBe(45.25m);
        root.GetProperty("nightPrice").GetDecimal().ShouldBe(60.5m);
        root.GetProperty("nightCallFee").GetDecimal().ShouldBe(10m);
        root.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        root.GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        root.GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        root.GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
    }

    [Fact]
    public async Task UpdatedContractWithoutARate_OmitsTheRateMembersFromTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var rate = Rate.Create(30m, null, null, new MinimumRule.None()).Value;
        var contract = await SaveAsync(NewFlatRateContract(customer.Id, nightSurcharge: false, rate));

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, BillingLevel.PerLocationPair, PricingMode.ByTrip, [], nightSurcharge: false,
                rate: null, [], new SystemClock(), FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(
            a => a.SubjectId == contract.Id.Value && a.EventType == AuditEventTypes.ContractUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;
        foreach (var member in new[]
                 {
                     "dayPrice", "nightPrice", "nightCallFee", "minimumRule", "minimumAmount",
                     "minimumThreshold", "minimumOveragePrice",
                 })
        {
            root.TryGetProperty(member, out _).ShouldBeFalse(member);
        }
    }

    private async Task<Vehicle> SeedVehicleAsync(string licensePlate, bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await VehicleTestSupport.SeedVehicleAsync(db, licensePlate, decommissioned: decommissioned);
    }

    private static VehicleRate NightRateWithTieredMinimum(Vehicle vehicle) =>
        new(vehicle.Id, Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value);

    private static VehicleRate RateWithoutMinimum(Vehicle vehicle) =>
        new(vehicle.Id, Rate.Create(48m, 58m, 4m, new MinimumRule.None()).Value);

    private static Contract NewContractWithVehicleRates(CustomerId customerId, params VehicleRate[] vehicleRates) =>
        Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(D(2026, 4, 1), null).Value,
            BillingLevel.FlatRate,
            PricingMode.ByHour,
            [],
            nightSurcharge: true,
            Rate.Create(45m, 60m, 10m, new MinimumRule.None()).Value,
            vehicleRates,
            new SystemClock(),
            FakeCurrentUser.ActorId).Value;

    private async Task<int> CountVehicleRateRowsAsync()
    {
        await using var read = CreateDbContext();
        return await read.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM registry.contract_vehicle_rates")
            .SingleAsync(Ct);
    }

    [Fact]
    public async Task VehicleRates_RoundTripThroughTheirOwnTable()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleAsync("AB456CD");
        var second = await SeedVehicleAsync("CD123EF");
        var withNight = NightRateWithTieredMinimum(first);
        var dayOnly = RateWithoutMinimum(second);
        var contract = await SaveAsync(NewContractWithVehicleRates(customer.Id, withNight, dayOnly));
        var otherCustomer = await SeedCustomerAsync(VatNumberB);
        var withoutVehicleRates = await SaveAsync(NewContractWithVehicleRates(otherCustomer.Id));

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        var loadedWithout = await read.Contracts.SingleAsync(c => c.Id == withoutVehicleRates.Id, Ct);

        // Assert
        loaded.VehicleRates.ShouldBe([withNight, dayOnly], ignoreOrder: true);
        loadedWithout.VehicleRates.ShouldBeEmpty();
        (await CountVehicleRateRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task VehicleRates_ChangedOnUpdate_ReplaceTheWholeList()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedVehicleAsync("AB456CD");
        var removed = await SeedVehicleAsync("CD123EF");
        var added = await SeedVehicleAsync("EF789GH");
        var contract = await SaveAsync(NewContractWithVehicleRates(
            customer.Id, RateWithoutMinimum(kept), RateWithoutMinimum(removed)));
        var repriced = new VehicleRate(kept.Id, Rate.Create(55m, 66m, 0m, new MinimumRule.FixedMinimum(120m)).Value);
        var addedRate = RateWithoutMinimum(added);

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, loaded.BillingLevel, loaded.PricingMode, loaded.RequiredDocuments,
                loaded.NightSurcharge, loaded.Rate, [repriced, addedRate], new SystemClock(), FakeCurrentUser.ActorId)
                .IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        reloaded.VehicleRates.ShouldBe([repriced, addedRate], ignoreOrder: true);
        (await CountVehicleRateRowsAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task VehicleRates_EmptiedOnUpdate_LeaveNoRows()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync("AB456CD");
        var contract = await SaveAsync(NewContractWithVehicleRates(customer.Id, RateWithoutMinimum(vehicle)));

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, loaded.BillingLevel, loaded.PricingMode, loaded.RequiredDocuments,
                loaded.NightSurcharge, loaded.Rate, [], new SystemClock(), FakeCurrentUser.ActorId)
                .IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct)).VehicleRates.ShouldBeEmpty();
        (await CountVehicleRateRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task VehicleRate_OnAnUnknownVehicleId_IsRefusedByTheForeignKey()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var unknown = new VehicleRate(VehicleId.New(), Rate.Create(48m, 60m, 5m, new MinimumRule.None()).Value);
        await using var db = CreateDbContext();
        db.Contracts.Add(NewContractWithVehicleRates(customer.Id, unknown));

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        // Assert
        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState
            .ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task DeletingAContract_RemovesItsVehicleRatesAndNoOthers()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync("AB456CD");
        var doomed = await SaveAsync(NewContractWithVehicleRates(customer.Id, RateWithoutMinimum(vehicle)));
        var other = Contract.Create(
            ContractId.New(), customer.Id, Validity.Create(D(2025, 1, 1), D(2025, 12, 31)).Value, BillingLevel.FlatRate,
            PricingMode.ByHour, [], true, Rate.Create(45m, 60m, 10m, new MinimumRule.None()).Value,
            [NightRateWithTieredMinimum(vehicle)], new SystemClock(), FakeCurrentUser.ActorId).Value;
        await SaveAsync(other);

        // Act
        await using (var db = CreateDbContext())
        {
            db.Contracts.Remove(await db.Contracts.SingleAsync(c => c.Id == doomed.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        (await read.Contracts.SingleAsync(c => c.Id == other.Id, Ct)).VehicleRates.Count.ShouldBe(1);
        (await CountVehicleRateRowsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreatedContractWithVehicleRates_WritesThemInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleAsync("AB456CD");
        var second = await SeedVehicleAsync("CD123EF");

        // Act
        var contract = await SaveAsync(NewContractWithVehicleRates(
            customer.Id, NightRateWithTieredMinimum(first), RateWithoutMinimum(second)));

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(
            a => a.SubjectId == contract.Id.Value && a.EventType == AuditEventTypes.ContractCreated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var items = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        var tiered = items.Single(i => i.GetProperty("vehicleId").GetGuid() == first.Id.Value);
        tiered.GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
        tiered.GetProperty("nightPrice").GetDecimal().ShouldBe(70.25m);
        tiered.GetProperty("nightCallFee").GetDecimal().ShouldBe(12m);
        tiered.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        tiered.GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        tiered.GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        tiered.GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
        var plain = items.Single(i => i.GetProperty("vehicleId").GetGuid() == second.Id.Value);
        plain.GetProperty("dayPrice").GetDecimal().ShouldBe(48m);
        plain.GetProperty("minimumRule").GetString().ShouldBe("None");
        plain.GetProperty("nightPrice").GetDecimal().ShouldBe(58m);
    }

    [Fact]
    public async Task UpdatedContract_WritesTheVehicleRatesAfterTheChangeInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleAsync("AB456CD");
        var second = await SeedVehicleAsync("CD123EF");
        var contract = await SaveAsync(NewContractWithVehicleRates(customer.Id, RateWithoutMinimum(first)));

        // Act
        await using (var db = CreateDbContext())
        {
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
            loaded.Update(
                loaded.Validity, loaded.BillingLevel, loaded.PricingMode, loaded.RequiredDocuments,
                loaded.NightSurcharge, loaded.Rate, [NightRateWithTieredMinimum(second)], new SystemClock(),
                FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(
            a => a.SubjectId == contract.Id.Value && a.EventType == AuditEventTypes.ContractUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var item = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().Single();
        item.GetProperty("vehicleId").GetGuid().ShouldBe(second.Id.Value);
        item.GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
    }

    [Fact]
    public async Task ContractWithoutVehicleRates_OmitsTheVehicleRatesFromTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();

        // Act
        var contract = await SaveAsync(NewContractWithVehicleRates(customer.Id));

        // Assert
        await using var read = CreateDbContext();
        var row = await read.AuditLog.SingleAsync(a => a.SubjectId == contract.Id.Value, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("vehicleRates", out _).ShouldBeFalse();
    }
}
