using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Contracts;

public sealed class LocationRateTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Validity OpenFromApril() => Validity.Create(new DateOnly(2026, 4, 1), null).Value;

    private static Rate NewRate(decimal? night = null, decimal? fee = null, MinimumRule? minimum = null) =>
        Rate.Create(50m, night, fee, minimum ?? new MinimumRule.None()).Value;

    private static LocationRate ForPickup(LocationId pickup, Rate? rate = null) =>
        new(pickup, null, rate ?? NewRate());

    private static LocationRate ForDelivery(LocationId delivery, Rate? rate = null) =>
        new(null, delivery, rate ?? NewRate());

    private static LocationRate ForPair(LocationId pickup, LocationId delivery, Rate? rate = null) =>
        new(pickup, delivery, rate ?? NewRate());

    // A rate of the shape the billing level asks for.
    private static LocationRate ForLevel(BillingLevel level, LocationId location, Rate? rate = null) => level switch
    {
        BillingLevel.PerPickupLocation => ForPickup(location, rate),
        BillingLevel.PerDeliveryLocation => ForDelivery(location, rate),
        _ => ForPair(location, LocationId.New(), rate),
    };

    private static Result<Contract> CreateWith(
        IReadOnlyList<LocationRate> locationRates,
        BillingLevel billingLevel = BillingLevel.PerPickupLocation,
        PricingMode pricingMode = PricingMode.ByHour,
        bool nightSurcharge = false,
        Rate? rate = null,
        IReadOnlyList<VehicleRate>? vehicleRates = null) =>
        Contract.Create(
            ContractId.New(), CustomerId.New(), OpenFromApril(), billingLevel, pricingMode, [],
            nightSurcharge, rate, vehicleRates ?? [], locationRates, NewClock(), Actor);

    private static Result UpdateWith(
        Contract contract,
        IReadOnlyList<LocationRate> locationRates,
        BillingLevel billingLevel = BillingLevel.PerPickupLocation,
        PricingMode pricingMode = PricingMode.ByHour,
        bool nightSurcharge = false,
        Rate? rate = null,
        FakeClock? clock = null) =>
        contract.Update(
            OpenFromApril(), billingLevel, pricingMode, [], nightSurcharge, rate, [], locationRates,
            clock ?? NewClock(), OtherActor);

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_LocationRatesOfTheLevel_AreHeldAndCarriedInTheEvent(BillingLevel level)
    {
        var first = ForLevel(level, LocationId.New());
        var second = ForLevel(level, LocationId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(150m)));

        var contract = CreateWith([first, second], level).Value;

        contract.LocationRates.ShouldBe([first, second]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>().LocationRates
            .ShouldBe([first, second]);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_LocationContractWithoutLocationRates_Fails(BillingLevel level)
    {
        var result = CreateWith([], level);

        result.Error.ShouldBe(ContractErrors.LocationRatesRequired);
    }

    [Fact]
    public void Update_LocationContractWithoutLocationRates_FailsAndLeavesTheContractUnchanged()
    {
        var kept = ForPickup(LocationId.New());
        var contract = CreateWith([kept]).Value;
        contract.ClearDomainEvents();

        var result = UpdateWith(contract, []);

        result.Error.ShouldBe(ContractErrors.LocationRatesRequired);
        contract.LocationRates.ShouldBe([kept]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_FlatRateContractWithoutLocationRates_HoldsAnEmptyList()
    {
        var contract = CreateWith([], BillingLevel.FlatRate, rate: NewRate()).Value;

        contract.LocationRates.ShouldBeEmpty();
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>().LocationRates.ShouldBeEmpty();
    }

    [Fact]
    public void Create_OppositePairsAndAPairOfTheSameLocation_AreDistinctRates()
    {
        var a = LocationId.New();
        var b = LocationId.New();

        var result = CreateWith([ForPair(a, b), ForPair(b, a), ForPair(a, a)], BillingLevel.PerLocationPair);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LocationRates.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation, false, false)]
    [InlineData(BillingLevel.PerPickupLocation, true, true)]
    [InlineData(BillingLevel.PerPickupLocation, false, true)]
    [InlineData(BillingLevel.PerDeliveryLocation, false, false)]
    [InlineData(BillingLevel.PerDeliveryLocation, true, true)]
    [InlineData(BillingLevel.PerDeliveryLocation, true, false)]
    [InlineData(BillingLevel.PerLocationPair, false, false)]
    [InlineData(BillingLevel.PerLocationPair, true, false)]
    [InlineData(BillingLevel.PerLocationPair, false, true)]
    public void Create_RateThatDoesNotNameExactlyTheLocationsOfTheLevel_Fails(
        BillingLevel level, bool hasPickup, bool hasDelivery)
    {
        var rate = new LocationRate(
            hasPickup ? LocationId.New() : null, hasDelivery ? LocationId.New() : null, NewRate());

        var result = CreateWith([rate], level);

        result.Error.ShouldBe(ContractErrors.LocationRateLocationsMismatch);
    }

    [Fact]
    public void Create_TheSamePickupLocationTwice_Fails()
    {
        var location = LocationId.New();

        var result = CreateWith([ForPickup(location), ForPickup(LocationId.New()), ForPickup(location)]);

        result.Error.ShouldBe(ContractErrors.LocationRateDuplicate);
    }

    [Fact]
    public void Create_TheSameDeliveryLocationTwice_Fails()
    {
        var location = LocationId.New();

        var result = CreateWith([ForDelivery(location), ForDelivery(location)], BillingLevel.PerDeliveryLocation);

        result.Error.ShouldBe(ContractErrors.LocationRateDuplicate);
    }

    [Fact]
    public void Create_TheSameOrderedPairTwice_Fails()
    {
        var a = LocationId.New();
        var b = LocationId.New();

        var result = CreateWith([ForPair(a, b), ForPair(b, a), ForPair(a, b)], BillingLevel.PerLocationPair);

        result.Error.ShouldBe(ContractErrors.LocationRateDuplicate);
    }

    [Fact]
    public void Update_TheSameLocationTwice_FailsAndLeavesTheContractUnchanged()
    {
        var original = ForPickup(LocationId.New());
        var contract = CreateWith([original]).Value;
        contract.ClearDomainEvents();
        var location = LocationId.New();

        var result = UpdateWith(contract, [ForPickup(location), ForPickup(location)]);

        result.Error.ShouldBe(ContractErrors.LocationRateDuplicate);
        contract.LocationRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_ReplacesTheLocationRatesAndCarriesThemInTheEvent()
    {
        var contract = CreateWith([ForPickup(LocationId.New())]).Value;
        contract.ClearDomainEvents();
        var replacement = ForPickup(LocationId.New());

        var result = UpdateWith(contract, [replacement]);

        result.IsSuccess.ShouldBeTrue();
        contract.LocationRates.ShouldBe([replacement]);
        contract.Version.ShouldBe(1);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().LocationRates
            .ShouldBe([replacement]);
    }

    [Fact]
    public void Update_WithTheSameTermsAndTheLocationRatesInAnotherOrder_ChangesNothing()
    {
        var clock = NewClock();
        var first = ForPickup(LocationId.New());
        var second = ForPickup(LocationId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(150m)));
        var contract = CreateWith([first, second]).Value;
        contract.ClearDomainEvents();
        var modifiedAt = contract.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = UpdateWith(
            contract,
            [ForPickup(second.PickupLocationId!.Value, NewRate(minimum: new MinimumRule.FixedMinimum(150m))), first],
            clock: clock);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(0);
        contract.LastModifiedAt.ShouldBe(modifiedAt);
        contract.LastModifiedBy.ShouldBe(Actor);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_ChangingOnlyALocationRate_IsAChange()
    {
        var location = LocationId.New();
        var contract = CreateWith([ForPickup(location)]).Value;
        contract.ClearDomainEvents();
        var changed = ForPickup(location, NewRate(minimum: new MinimumRule.FixedMinimum(150m)));

        var result = UpdateWith(contract, [changed]);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(1);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().LocationRates
            .ShouldBe([changed]);
    }

    [Fact]
    public void CloseOn_KeepsTheLocationRates()
    {
        var locationRate = ForPickup(LocationId.New());
        var contract = CreateWith([locationRate]).Value;
        contract.ClearDomainEvents();

        contract.CloseOn(new DateOnly(2026, 12, 31), NewClock(), OtherActor).IsSuccess.ShouldBeTrue();

        contract.LocationRates.ShouldBe([locationRate]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().LocationRates
            .ShouldBe([locationRate]);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(55, null)]
    [InlineData(null, null)]
    public void Create_LocationRateWithNightSurchargeAndMissingNightValues_Fails(int? night, int? fee)
    {
        var result = CreateWith([ForPickup(LocationId.New(), NewRate(night, fee))], nightSurcharge: true);

        result.Error.ShouldBe(ContractErrors.NightRateMissing);
    }

    [Theory]
    [InlineData(55, 5)]
    [InlineData(55, null)]
    [InlineData(null, 0)]
    public void Create_LocationRateWithNightValuesWithoutNightSurcharge_Fails(int? night, int? fee)
    {
        var result = CreateWith([ForPickup(LocationId.New(), NewRate(night, fee))]);

        result.Error.ShouldBe(ContractErrors.NightRateNotApplicable);
    }

    [Fact]
    public void Create_LocationRateWithBothNightValuesAndNightSurcharge_Succeeds()
    {
        var locationRate = ForPickup(LocationId.New(), NewRate(65m, 0m));

        var contract = CreateWith([locationRate], nightSurcharge: true).Value;

        contract.LocationRates.ShouldBe([locationRate]);
    }

    [Fact]
    public void Create_ByTripLocationRateWithAMinimum_Fails()
    {
        var locationRate = ForPickup(LocationId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(100m)));

        var result = CreateWith([locationRate], pricingMode: PricingMode.ByTrip);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
    }

    [Fact]
    public void Create_ByTripLocationRateWithNoMinimum_Succeeds()
    {
        CreateWith([ForPickup(LocationId.New())], pricingMode: PricingMode.ByTrip).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_LocationRateThatDoesNotFit_FailsAndLeavesTheContractUnchanged()
    {
        var original = ForPickup(LocationId.New());
        var contract = CreateWith([original]).Value;
        contract.ClearDomainEvents();
        var badRate = ForPickup(LocationId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(100m)));

        var result = UpdateWith(contract, [badRate], pricingMode: PricingMode.ByTrip);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
        contract.PricingMode.ShouldBe(PricingMode.ByHour);
        contract.LocationRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_LocationRatesOnAFlatRateContract_Fails()
    {
        var result = CreateWith([ForPickup(LocationId.New())], BillingLevel.FlatRate, rate: NewRate());

        result.Error.ShouldBe(ContractErrors.LocationRateNotApplicable);
    }

    [Fact]
    public void Update_LocationRatesOnAFlatRateContract_FailsAndLeavesTheContractUnchanged()
    {
        var original = ForPickup(LocationId.New());
        var contract = CreateWith([original]).Value;
        contract.ClearDomainEvents();

        var result = UpdateWith(contract, [original], BillingLevel.FlatRate, rate: NewRate());

        result.Error.ShouldBe(ContractErrors.LocationRateNotApplicable);
        contract.BillingLevel.ShouldBe(BillingLevel.PerPickupLocation);
        contract.LocationRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_ContractLevelRateOnALocationContract_Fails(BillingLevel level)
    {
        var result = CreateWith([ForLevel(level, LocationId.New())], level, rate: NewRate());

        result.Error.ShouldBe(ContractErrors.RateNotApplicable);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_VehicleRatesOnALocationContract_Fail(BillingLevel level)
    {
        var result = CreateWith(
            [ForLevel(level, LocationId.New())], level, vehicleRates: [new VehicleRate(VehicleId.New(), NewRate())]);

        result.Error.ShouldBe(ContractErrors.VehicleRateNotApplicable);
    }
}
