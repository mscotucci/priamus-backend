using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
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
            new SystemClock(),
            FakeCurrentUser.ActorId);

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
                [RequiredDocument.DeliveryNote, RequiredDocument.DailySummary], loaded.NightSurcharge,
                new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        // Assert
        await using var read = CreateDbContext();
        var reloaded = await read.Contracts.SingleAsync(c => c.Id == contract.Id, Ct);
        reloaded.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.DailySummary]);
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
                [], nightSurcharge: false, new SystemClock(), FakeCurrentUser.ActorId);
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
}
