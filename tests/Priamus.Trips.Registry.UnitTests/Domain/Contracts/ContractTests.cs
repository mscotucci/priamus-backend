using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Contracts;

public sealed class ContractTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Validity V(string from, string? to) =>
        Validity.Create(DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to)).Value;

    private static Contract NewContract(
        FakeClock clock,
        CustomerId? customerId = null,
        Validity? validity = null,
        IEnumerable<RequiredDocument>? documents = null,
        bool nightSurcharge = false) =>
        Contract.Create(
            ContractId.New(),
            customerId ?? CustomerId.New(),
            validity ?? V("2026-04-01", null),
            BillingLevel.FlatRate,
            PricingMode.ByHour,
            documents ?? [RequiredDocument.DeliveryNote],
            nightSurcharge,
            clock,
            Actor);

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HoldsAllTermsAndStampsAuditFields()
    {
        var clock = NewClock();
        var id = ContractId.New();
        var customerId = CustomerId.New();
        var validity = V("2026-04-01", "2026-12-31");

        var contract = Contract.Create(id, customerId, validity, BillingLevel.PerLocationPair,
            PricingMode.ByWeight, [RequiredDocument.WasteForm], true, clock, Actor);

        contract.Id.ShouldBe(id);
        contract.CustomerId.ShouldBe(customerId);
        contract.Validity.ShouldBe(validity);
        contract.BillingLevel.ShouldBe(BillingLevel.PerLocationPair);
        contract.PricingMode.ShouldBe(PricingMode.ByWeight);
        contract.RequiredDocuments.ShouldBe([RequiredDocument.WasteForm]);
        contract.NightSurcharge.ShouldBeTrue();
        contract.CreatedAt.ShouldBe(clock.UtcNow);
        contract.CreatedBy.ShouldBe(Actor);
        contract.LastModifiedAt.ShouldBe(clock.UtcNow);
        contract.LastModifiedBy.ShouldBe(Actor);
        contract.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithNoDocuments_HoldsAnEmptySet()
    {
        var contract = NewContract(NewClock(), documents: []);

        contract.RequiredDocuments.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllDocuments_HoldsAllInCanonicalOrder()
    {
        var contract = NewContract(NewClock(), documents:
            [RequiredDocument.DailySummary, RequiredDocument.WasteForm, RequiredDocument.DeliverySlip,
             RequiredDocument.DeliveryNote]);

        contract.RequiredDocuments.ShouldBe(
            [RequiredDocument.DeliveryNote, RequiredDocument.DeliverySlip, RequiredDocument.WasteForm,
             RequiredDocument.DailySummary]);
    }

    [Fact]
    public void Create_WithDuplicateDocuments_CollapsesThem()
    {
        var contract = NewContract(NewClock(), documents:
            [RequiredDocument.WasteForm, RequiredDocument.DeliveryNote, RequiredDocument.WasteForm]);

        contract.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.WasteForm]);
    }

    [Fact]
    public void Create_RaisesContractCreatedWithFullState()
    {
        var clock = NewClock();
        var contract = NewContract(clock, validity: V("2026-04-01", "2026-12-31"), nightSurcharge: true,
            documents: [RequiredDocument.DeliverySlip]);

        var created = contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>();
        created.ContractId.ShouldBe(contract.Id);
        created.CustomerId.ShouldBe(contract.CustomerId);
        created.OccurredAt.ShouldBe(clock.UtcNow);
        created.ValidFrom.ShouldBe(new DateOnly(2026, 4, 1));
        created.ValidTo.ShouldBe(new DateOnly(2026, 12, 31));
        created.BillingLevel.ShouldBe(BillingLevel.FlatRate);
        created.PricingMode.ShouldBe(PricingMode.ByHour);
        created.RequiredDocuments.ShouldBe([RequiredDocument.DeliverySlip]);
        created.NightSurcharge.ShouldBeTrue();
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ReplacesTermsBumpsVersionAndRaisesContractUpdated()
    {
        var clock = NewClock();
        var contract = NewContract(clock);
        contract.ClearDomainEvents();
        clock.Advance(TimeSpan.FromHours(2));

        contract.Update(V("2026-05-01", "2026-09-30"), BillingLevel.PerPickupLocation, PricingMode.ByTrip,
            [RequiredDocument.DailySummary, RequiredDocument.DeliveryNote, RequiredDocument.DailySummary],
            true, clock, OtherActor);

        contract.Validity.ShouldBe(V("2026-05-01", "2026-09-30"));
        contract.BillingLevel.ShouldBe(BillingLevel.PerPickupLocation);
        contract.PricingMode.ShouldBe(PricingMode.ByTrip);
        contract.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.DailySummary]);
        contract.NightSurcharge.ShouldBeTrue();
        contract.LastModifiedAt.ShouldBe(clock.UtcNow);
        contract.LastModifiedBy.ShouldBe(OtherActor);
        contract.CreatedBy.ShouldBe(Actor);
        contract.Version.ShouldBe(1);

        var updated = contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>();
        updated.ContractId.ShouldBe(contract.Id);
        updated.ValidTo.ShouldBe(new DateOnly(2026, 9, 30));
        updated.BillingLevel.ShouldBe(BillingLevel.PerPickupLocation);
        updated.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.DailySummary]);
        updated.NightSurcharge.ShouldBeTrue();
    }

    // ── CloseOn ──────────────────────────────────────────────────────────────

    [Fact]
    public void CloseOn_EndsTheValidityBumpsVersionAndRaisesContractUpdated()
    {
        var clock = NewClock();
        var contract = NewContract(clock, validity: V("2026-04-01", null));
        contract.ClearDomainEvents();

        var result = contract.CloseOn(new DateOnly(2026, 12, 31), clock, OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Validity.ValidTo.ShouldBe(new DateOnly(2026, 12, 31));
        contract.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 4, 1));
        contract.Version.ShouldBe(1);
        contract.LastModifiedBy.ShouldBe(OtherActor);
        var updated = contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>();
        updated.ValidTo.ShouldBe(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void CloseOn_BeforeTheStart_FailsAndLeavesTheContractUnchanged()
    {
        var clock = NewClock();
        var contract = NewContract(clock, validity: V("2026-04-01", null));
        contract.ClearDomainEvents();

        var result = contract.CloseOn(new DateOnly(2026, 3, 31), clock, OtherActor);

        result.Error.ShouldBe(ContractErrors.ValidityEndBeforeStart);
        contract.Validity.ValidTo.ShouldBeNull();
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }
}
