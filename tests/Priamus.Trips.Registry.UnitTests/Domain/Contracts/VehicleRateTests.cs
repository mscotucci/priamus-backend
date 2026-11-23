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

public sealed class VehicleRateTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Validity OpenFromApril() => Validity.Create(new DateOnly(2026, 4, 1), null).Value;

    private static Rate NewRate(decimal? night = null, decimal? fee = null, MinimumRule? minimum = null) =>
        Rate.Create(50m, night, fee, minimum ?? new MinimumRule.None()).Value;

    private static VehicleRate ForVehicle(VehicleId vehicleId, Rate? rate = null) =>
        new(vehicleId, null, null, rate ?? NewRate());

    private static Result<Contract> CreateWith(
        IReadOnlyList<VehicleRate> vehicleRates,
        BillingLevel billingLevel = BillingLevel.FlatRate,
        PricingMode pricingMode = PricingMode.ByHour,
        bool nightSurcharge = false) =>
        Contract.Create(
            ContractId.New(), CustomerId.New(), OpenFromApril(), billingLevel, pricingMode, [],
            nightSurcharge,
            billingLevel == BillingLevel.FlatRate
                ? (nightSurcharge ? NewRate(55m, 5m) : NewRate())
                : null,
            vehicleRates, [], NewClock(), Actor);

    [Fact]
    public void Create_FlatRateWithoutVehicleRates_HoldsAnEmptyList()
    {
        var contract = CreateWith([]).Value;

        contract.VehicleRates.ShouldBeEmpty();
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>().VehicleRates.ShouldBeEmpty();
    }

    [Fact]
    public void Create_FlatRateWithSeveralVehicleRates_HoldsThemAndCarriesThemInTheEvent()
    {
        var first = ForVehicle(VehicleId.New());
        var second = ForVehicle(VehicleId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(150m)));

        var contract = CreateWith([first, second]).Value;

        contract.VehicleRates.ShouldBe([first, second]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>().VehicleRates
            .ShouldBe([first, second]);
    }

    [Fact]
    public void Update_ReplacesTheVehicleRatesAndCarriesThemInTheEvent()
    {
        var clock = NewClock();
        var contract = CreateWith([ForVehicle(VehicleId.New())]).Value;
        contract.ClearDomainEvents();
        var replacement = ForVehicle(VehicleId.New());

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByHour, [], false,
            NewRate(), [replacement], [], clock, OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.VehicleRates.ShouldBe([replacement]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().VehicleRates
            .ShouldBe([replacement]);
    }

    [Fact]
    public void Update_WithTheSameTermsAndTheVehicleRatesInAnotherOrder_ChangesNothing()
    {
        var clock = NewClock();
        var first = ForVehicle(VehicleId.New());
        var second = ForVehicle(VehicleId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(150m)));
        var contract = CreateWith([first, second]).Value;
        contract.ClearDomainEvents();
        var modifiedAt = contract.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByHour, [], false,
            NewRate(), [ForVehicle(second.VehicleId, NewRate(minimum: new MinimumRule.FixedMinimum(150m))), first],
            [], clock, OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(0);
        contract.LastModifiedAt.ShouldBe(modifiedAt);
        contract.LastModifiedBy.ShouldBe(Actor);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_ChangingOnlyAVehicleRate_IsAChange()
    {
        var vehicleId = VehicleId.New();
        var contract = CreateWith([ForVehicle(vehicleId)]).Value;
        contract.ClearDomainEvents();
        var changed = ForVehicle(vehicleId, NewRate(minimum: new MinimumRule.FixedMinimum(150m)));

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByHour, [], false,
            NewRate(), [changed], [], NewClock(), OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(1);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().VehicleRates
            .ShouldBe([changed]);
    }

    [Fact]
    public void Update_WithNoVehicleRates_RemovesTheExistingOnes()
    {
        var contract = CreateWith([ForVehicle(VehicleId.New())]).Value;

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByHour, [], false,
            NewRate(), [], [], NewClock(), OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.VehicleRates.ShouldBeEmpty();
    }

    [Fact]
    public void CloseOn_KeepsTheVehicleRates()
    {
        var vehicleRate = ForVehicle(VehicleId.New());
        var contract = CreateWith([vehicleRate]).Value;
        contract.ClearDomainEvents();

        contract.CloseOn(new DateOnly(2026, 12, 31), NewClock(), OtherActor).IsSuccess.ShouldBeTrue();

        contract.VehicleRates.ShouldBe([vehicleRate]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().VehicleRates
            .ShouldBe([vehicleRate]);
    }

    private static readonly LocationId Destination = LocationId.New();

    // The vehicle rate and the location rate of the shape the billing level asks for, on one location.
    private static VehicleRate ForVehicleAt(BillingLevel level, VehicleId vehicleId, LocationId location, Rate? rate = null) =>
        level switch
        {
            BillingLevel.PerPickupLocation => new(vehicleId, location, null, rate ?? NewRate()),
            BillingLevel.PerDeliveryLocation => new(vehicleId, null, location, rate ?? NewRate()),
            _ => new(vehicleId, location, Destination, rate ?? NewRate()),
        };

    private static LocationRate LocationRateAt(BillingLevel level, LocationId location) =>
        level switch
        {
            BillingLevel.PerPickupLocation => new(location, null, NewRate()),
            BillingLevel.PerDeliveryLocation => new(null, location, NewRate()),
            _ => new(location, Destination, NewRate()),
        };

    private static Result<Contract> CreateAt(
        BillingLevel level,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationId> ratedLocations,
        PricingMode pricingMode = PricingMode.ByHour,
        bool nightSurcharge = false) =>
        Contract.Create(
            ContractId.New(), CustomerId.New(), OpenFromApril(), level, pricingMode, [], nightSurcharge, null,
            vehicleRates, [.. ratedLocations.Select(l => LocationRateAt(level, l))], NewClock(), Actor);

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_LocationContractWithVehicleRatesOnItsLocations_HoldsThemAndCarriesThemInTheEvent(BillingLevel level)
    {
        var first = LocationId.New();
        var second = LocationId.New();
        var tanker = VehicleId.New();
        var fixedMinimum = NewRate(minimum: new MinimumRule.FixedMinimum(150m));
        var vehicleRates = new[]
        {
            ForVehicleAt(level, tanker, first, fixedMinimum),
            ForVehicleAt(level, tanker, second, fixedMinimum),
            ForVehicleAt(level, VehicleId.New(), second),
        };

        var contract = CreateAt(level, vehicleRates, [first, second]).Value;

        contract.VehicleRates.ShouldBe(vehicleRates);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractCreated>().VehicleRates
            .ShouldBe(vehicleRates);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_LocationContractWithoutVehicleRates_IsValid(BillingLevel level)
    {
        var contract = CreateAt(level, [], [LocationId.New()]).Value;

        contract.VehicleRates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_TheSameVehicleTwiceOnOneLocationOrPair_Fails(BillingLevel level)
    {
        var location = LocationId.New();
        var vehicleId = VehicleId.New();

        var result = CreateAt(
            level,
            [
                ForVehicleAt(level, vehicleId, location),
                ForVehicleAt(level, vehicleId, location, NewRate(minimum: new MinimumRule.FixedMinimum(100m))),
            ],
            [location]);

        result.Error.ShouldBe(ContractErrors.VehicleRateDuplicate);
    }

    [Fact]
    public void Create_TheSameVehicleOnAToBAndBToA_IsValid()
    {
        var a = LocationId.New();
        var b = LocationId.New();
        var vehicleId = VehicleId.New();
        var vehicleRates = new VehicleRate[]
        {
            new(vehicleId, a, b, NewRate()),
            new(vehicleId, b, a, NewRate()),
        };

        var result = Contract.Create(
            ContractId.New(), CustomerId.New(), OpenFromApril(), BillingLevel.PerLocationPair, PricingMode.ByHour, [],
            false, null, vehicleRates, [new LocationRate(a, b, NewRate()), new LocationRate(b, a, NewRate())],
            NewClock(), Actor);

        result.Value.VehicleRates.ShouldBe(vehicleRates);
    }

    [Theory]
    [InlineData(BillingLevel.FlatRate, true, false)]
    [InlineData(BillingLevel.FlatRate, false, true)]
    [InlineData(BillingLevel.PerPickupLocation, false, false)]
    [InlineData(BillingLevel.PerPickupLocation, true, true)]
    [InlineData(BillingLevel.PerPickupLocation, false, true)]
    [InlineData(BillingLevel.PerDeliveryLocation, false, false)]
    [InlineData(BillingLevel.PerDeliveryLocation, true, true)]
    [InlineData(BillingLevel.PerDeliveryLocation, true, false)]
    [InlineData(BillingLevel.PerLocationPair, false, false)]
    [InlineData(BillingLevel.PerLocationPair, true, false)]
    [InlineData(BillingLevel.PerLocationPair, false, true)]
    public void Create_VehicleRateWithTheWrongSidesForTheLevel_Fails(BillingLevel level, bool pickup, bool delivery)
    {
        var location = LocationId.New();
        var vehicleRate = new VehicleRate(VehicleId.New(), pickup ? location : null, delivery ? location : null, NewRate());
        IReadOnlyList<LocationRate> locationRates = level == BillingLevel.FlatRate ? [] : [LocationRateAt(level, location)];

        var result = Contract.Create(
            ContractId.New(), CustomerId.New(), OpenFromApril(), level, PricingMode.ByHour, [], false,
            level == BillingLevel.FlatRate ? NewRate() : null, [vehicleRate], locationRates, NewClock(), Actor);

        result.Error.ShouldBe(ContractErrors.VehicleRateLocationsMismatch);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_VehicleRateOnALocationTheContractHasNoRateFor_Fails(BillingLevel level)
    {
        var result = CreateAt(level, [ForVehicleAt(level, VehicleId.New(), LocationId.New())], [LocationId.New()]);

        result.Error.ShouldBe(ContractErrors.VehicleRateWithoutLocationRate);
    }

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_LocationContractVehicleRateJudgedByTheNightAndMinimumRulesOfTheContract(BillingLevel level)
    {
        var location = LocationId.New();

        CreateAt(level, [ForVehicleAt(level, VehicleId.New(), location)], [location], nightSurcharge: true)
            .Error.ShouldBe(ContractErrors.NightRateMissing);
        CreateAt(level, [ForVehicleAt(level, VehicleId.New(), location, NewRate(55m, 5m))], [location])
            .Error.ShouldBe(ContractErrors.NightRateNotApplicable);
        CreateAt(
            level, [ForVehicleAt(level, VehicleId.New(), location, NewRate(minimum: new MinimumRule.FixedMinimum(100m)))],
            [location], PricingMode.ByTrip).Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
    }

    [Fact]
    public void Update_LocationContractWithTheSameVehicleRatesInAnotherOrder_ChangesNothing()
    {
        var level = BillingLevel.PerPickupLocation;
        var first = LocationId.New();
        var second = LocationId.New();
        var vehicleId = VehicleId.New();
        var onFirst = ForVehicleAt(level, vehicleId, first);
        var onSecond = ForVehicleAt(level, vehicleId, second);
        var contract = CreateAt(level, [onFirst, onSecond], [first, second]).Value;
        contract.ClearDomainEvents();

        var result = contract.Update(
            OpenFromApril(), level, PricingMode.ByHour, [], false, null, [onSecond, onFirst],
            [LocationRateAt(level, second), LocationRateAt(level, first)], NewClock(), OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_ChangingOnlyTheLocationOfAVehicleRate_IsAChange()
    {
        var level = BillingLevel.PerPickupLocation;
        var first = LocationId.New();
        var second = LocationId.New();
        var vehicleId = VehicleId.New();
        var contract = CreateAt(level, [ForVehicleAt(level, vehicleId, first)], [first, second]).Value;
        contract.ClearDomainEvents();
        var moved = ForVehicleAt(level, vehicleId, second);

        var result = contract.Update(
            OpenFromApril(), level, PricingMode.ByHour, [], false, null, [moved],
            [LocationRateAt(level, first), LocationRateAt(level, second)], NewClock(), OtherActor);

        result.IsSuccess.ShouldBeTrue();
        contract.Version.ShouldBe(1);
        contract.VehicleRates.ShouldBe([moved]);
        contract.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ContractUpdated>().VehicleRates.ShouldBe([moved]);
    }

    [Fact]
    public void Update_VehicleRateOnALocationTheContractHasNoRateFor_FailsAndLeavesTheContractUnchanged()
    {
        var level = BillingLevel.PerPickupLocation;
        var location = LocationId.New();
        var kept = ForVehicleAt(level, VehicleId.New(), location);
        var contract = CreateAt(level, [kept], [location]).Value;
        contract.ClearDomainEvents();

        var result = contract.Update(
            OpenFromApril(), level, PricingMode.ByHour, [], false, null,
            [ForVehicleAt(level, VehicleId.New(), LocationId.New())], [LocationRateAt(level, location)],
            NewClock(), OtherActor);

        result.Error.ShouldBe(ContractErrors.VehicleRateWithoutLocationRate);
        contract.VehicleRates.ShouldBe([kept]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_TheSameVehicleTwice_Fails()
    {
        var vehicleId = VehicleId.New();

        var result = CreateWith([ForVehicle(vehicleId), ForVehicle(VehicleId.New()), ForVehicle(vehicleId)]);

        result.Error.ShouldBe(ContractErrors.VehicleRateDuplicate);
    }

    [Fact]
    public void Update_TheSameVehicleTwice_FailsAndLeavesTheContractUnchanged()
    {
        var original = ForVehicle(VehicleId.New());
        var contract = CreateWith([original]).Value;
        contract.ClearDomainEvents();
        var vehicleId = VehicleId.New();

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByHour, [], false,
            NewRate(), [ForVehicle(vehicleId), ForVehicle(vehicleId)], [], NewClock(), OtherActor);

        result.Error.ShouldBe(ContractErrors.VehicleRateDuplicate);
        contract.VehicleRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, null, 5)]
    [InlineData(true, 55, null)]
    [InlineData(true, null, null)]
    public void Create_VehicleRateWithNightSurchargeAndMissingNightValues_Fails(bool surcharge, int? night, int? fee)
    {
        var result = CreateWith([ForVehicle(VehicleId.New(), NewRate(night, fee))], nightSurcharge: surcharge);

        result.Error.ShouldBe(ContractErrors.NightRateMissing);
    }

    [Theory]
    [InlineData(55, 5)]
    [InlineData(55, null)]
    [InlineData(null, 0)]
    public void Create_VehicleRateWithNightValuesWithoutNightSurcharge_Fails(int? night, int? fee)
    {
        var result = CreateWith([ForVehicle(VehicleId.New(), NewRate(night, fee))]);

        result.Error.ShouldBe(ContractErrors.NightRateNotApplicable);
    }

    [Fact]
    public void Create_VehicleRateWithBothNightValuesAndNightSurcharge_Succeeds()
    {
        var vehicleRate = ForVehicle(VehicleId.New(), NewRate(65m, 0m));

        var contract = CreateWith([vehicleRate], nightSurcharge: true).Value;

        contract.VehicleRates.ShouldBe([vehicleRate]);
    }

    [Fact]
    public void Create_ByTripVehicleRateWithAMinimum_Fails()
    {
        var vehicleRate = ForVehicle(VehicleId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(100m)));

        var result = CreateWith([vehicleRate], pricingMode: PricingMode.ByTrip);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
    }

    [Fact]
    public void Create_ByTripVehicleRateWithNoMinimum_Succeeds()
    {
        CreateWith([ForVehicle(VehicleId.New())], pricingMode: PricingMode.ByTrip).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_VehicleRateThatDoesNotFit_FailsAndLeavesTheContractUnchanged()
    {
        var original = ForVehicle(VehicleId.New());
        var contract = CreateWith([original]).Value;
        contract.ClearDomainEvents();
        var badRate = ForVehicle(VehicleId.New(), NewRate(minimum: new MinimumRule.FixedMinimum(100m)));

        var result = contract.Update(OpenFromApril(), BillingLevel.FlatRate, PricingMode.ByTrip, [], false,
            NewRate(), [badRate], [], NewClock(), OtherActor);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
        contract.PricingMode.ShouldBe(PricingMode.ByHour);
        contract.VehicleRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }
}
