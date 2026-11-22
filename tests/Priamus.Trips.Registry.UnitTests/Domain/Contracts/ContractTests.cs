using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Results;
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
        bool nightSurcharge = false,
        Rate? rate = null,
        BillingLevel billingLevel = BillingLevel.FlatRate,
        PricingMode pricingMode = PricingMode.ByHour) =>
        Contract.Create(
            ContractId.New(),
            customerId ?? CustomerId.New(),
            validity ?? V("2026-04-01", null),
            billingLevel,
            pricingMode,
            documents ?? [RequiredDocument.DeliveryNote],
            nightSurcharge,
            rate ?? (billingLevel == BillingLevel.FlatRate ? DefaultRate(nightSurcharge) : null),
            [],
            LocationRatesFor(billingLevel, nightSurcharge),
            clock,
            Actor).Value;

    private static Rate DefaultRate(bool nightSurcharge) =>
        nightSurcharge ? NewRate(55m, 5m) : NewRate();

    // The one location rate a contract of a location level needs; none for a flat-rate contract.
    private static IReadOnlyList<LocationRate> LocationRatesFor(BillingLevel billingLevel, bool nightSurcharge) =>
        billingLevel switch
        {
            BillingLevel.FlatRate => [],
            BillingLevel.PerPickupLocation => [new LocationRate(LocationId.New(), null, DefaultRate(nightSurcharge))],
            BillingLevel.PerDeliveryLocation => [new LocationRate(null, LocationId.New(), DefaultRate(nightSurcharge))],
            _ => [new LocationRate(LocationId.New(), LocationId.New(), DefaultRate(nightSurcharge))],
        };

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HoldsAllTermsAndStampsAuditFields()
    {
        var clock = NewClock();
        var id = ContractId.New();
        var customerId = CustomerId.New();
        var validity = V("2026-04-01", "2026-12-31");

        var contract = Contract.Create(id, customerId, validity, BillingLevel.PerLocationPair,
            PricingMode.ByWeight, [RequiredDocument.WasteForm], true, null, [],
            LocationRatesFor(BillingLevel.PerLocationPair, true), clock, Actor).Value;

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
            true, null, [], LocationRatesFor(BillingLevel.PerPickupLocation, true), clock, OtherActor);

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

    // ── Rate ─────────────────────────────────────────────────────────────────

    private static Rate NewRate(decimal? night = null, decimal? fee = null, MinimumRule? minimum = null) =>
        Rate.Create(45m, night, fee, minimum ?? new MinimumRule.None()).Value;

    private static Result<Contract> CreateWith(
        BillingLevel billingLevel, PricingMode pricingMode, bool nightSurcharge, Rate? rate) =>
        Contract.Create(ContractId.New(), CustomerId.New(), V("2026-04-01", null), billingLevel, pricingMode,
            [], nightSurcharge, rate, [], LocationRatesFor(billingLevel, nightSurcharge), NewClock(), Actor);

    [Fact]
    public void Create_WithRate_HoldsItAndCarriesItInTheEvent()
    {
        var rate = NewRate(55m, 5m, new MinimumRule.FixedMinimum(120m));

        var contract = NewContract(NewClock(), nightSurcharge: true, rate: rate);

        contract.Rate.ShouldBe(rate);
        var created = contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>();
        created.Rate.ShouldBe(rate);
    }

    [Fact]
    public void Update_WithRate_ReplacesItAndCarriesItInTheEvent()
    {
        var clock = NewClock();
        var contract = NewContract(clock, rate: NewRate());
        contract.ClearDomainEvents();
        var newRate = NewRate(minimum: new MinimumRule.TieredOverage(300m, 4m, 60m));

        var result = contract.Update(V("2026-04-01", null), BillingLevel.FlatRate, PricingMode.ByHour,
            [], false, newRate, [],
            [],
            clock, OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Rate.ShouldBe(newRate);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().Rate.ShouldBe(newRate);
    }

    [Fact]
    public void Create_FlatRateWithoutRate_Fails()
    {
        var result = CreateWith(BillingLevel.FlatRate, PricingMode.ByHour, false, null);

        result.Error.ShouldBe(ContractErrors.RateRequired);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_NonFlatWithoutRate_Succeeds(BillingLevel level)
    {
        CreateWith(level, PricingMode.ByHour, false, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_FlatRateWithoutRate_FailsAndLeavesTheContractUnchanged()
    {
        var clock = NewClock();
        var original = NewRate();
        var contract = NewContract(clock, rate: original);
        contract.ClearDomainEvents();

        var result = contract.Update(V("2026-05-01", null), BillingLevel.FlatRate, PricingMode.ByHour,
            [], false, null, [],
            [],
            clock, OtherActor);

        result.Error.ShouldBe(ContractErrors.RateRequired);
        contract.Rate.ShouldBe(original);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_WithRateOnANonFlatBillingLevel_Fails(BillingLevel level)
    {
        var result = CreateWith(level, PricingMode.ByHour, false, NewRate());

        result.Error.ShouldBe(ContractErrors.RateNotApplicable);
    }

    [Theory]
    [InlineData(true, null, 5)]
    [InlineData(true, 55, null)]
    [InlineData(true, null, null)]
    public void Create_WithNightSurchargeAndMissingNightValues_Fails(bool surcharge, int? night, int? fee)
    {
        var result = CreateWith(BillingLevel.FlatRate, PricingMode.ByHour, surcharge, NewRate(night, fee));

        result.Error.ShouldBe(ContractErrors.NightRateMissing);
    }

    [Theory]
    [InlineData(55, 5)]
    [InlineData(55, null)]
    [InlineData(null, 0)]
    public void Create_WithoutNightSurchargeAndNightValues_Fails(int? night, int? fee)
    {
        var result = CreateWith(BillingLevel.FlatRate, PricingMode.ByHour, false, NewRate(night, fee));

        result.Error.ShouldBe(ContractErrors.NightRateNotApplicable);
    }

    [Fact]
    public void Create_WithNightSurchargeAndBothNightValues_Succeeds()
    {
        CreateWith(BillingLevel.FlatRate, PricingMode.ByHour, true, NewRate(55m, 0m)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_ByTripWithAMinimum_Fails()
    {
        var result = CreateWith(BillingLevel.FlatRate, PricingMode.ByTrip, false,
            NewRate(minimum: new MinimumRule.FixedMinimum(100m)));

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
    }

    [Fact]
    public void Create_ByTripWithNoMinimum_Succeeds()
    {
        CreateWith(BillingLevel.FlatRate, PricingMode.ByTrip, false, NewRate()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_WithARateThatDoesNotFit_FailsAndLeavesTheContractUnchanged()
    {
        var clock = NewClock();
        var original = NewRate();
        var contract = NewContract(clock, rate: original);
        contract.ClearDomainEvents();

        var result = contract.Update(V("2026-05-01", null), BillingLevel.FlatRate, PricingMode.ByTrip,
            [], false, NewRate(minimum: new MinimumRule.FixedMinimum(100m)), [],
            [],
            clock, OtherActor);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
        contract.Rate.ShouldBe(original);
        contract.Validity.ShouldBe(V("2026-04-01", null));
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void CloseOn_KeepsTheRate()
    {
        var rate = NewRate();
        var contract = NewContract(NewClock(), rate: rate);

        contract.CloseOn(new DateOnly(2026, 12, 31), NewClock(), OtherActor);

        contract.Rate.ShouldBe(rate);
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
