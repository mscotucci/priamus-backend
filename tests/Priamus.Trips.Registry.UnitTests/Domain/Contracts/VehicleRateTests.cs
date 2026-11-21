using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
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
        new(vehicleId, rate ?? NewRate());

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
            vehicleRates, NewClock(), Actor);

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
            NewRate(), [replacement], clock, OtherActor);

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
            clock, OtherActor);

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
            NewRate(), [changed], NewClock(), OtherActor);

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
            NewRate(), [], NewClock(), OtherActor);

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

    [Theory]
    [InlineData(BillingLevel.PerPickupLocation)]
    [InlineData(BillingLevel.PerDeliveryLocation)]
    [InlineData(BillingLevel.PerLocationPair)]
    public void Create_VehicleRatesOnANonFlatBillingLevel_Fails(BillingLevel level)
    {
        var result = CreateWith([ForVehicle(VehicleId.New())], level);

        result.Error.ShouldBe(ContractErrors.VehicleRateNotApplicable);
    }

    [Fact]
    public void Update_VehicleRatesOnANonFlatBillingLevel_FailsAndLeavesTheContractUnchanged()
    {
        var kept = ForVehicle(VehicleId.New());
        var contract = CreateWith([kept]).Value;
        contract.ClearDomainEvents();

        var result = contract.Update(OpenFromApril(), BillingLevel.PerLocationPair, PricingMode.ByHour, [], false,
            null, [kept], NewClock(), OtherActor);

        result.Error.ShouldBe(ContractErrors.VehicleRateNotApplicable);
        contract.BillingLevel.ShouldBe(BillingLevel.FlatRate);
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
            NewRate(), [ForVehicle(vehicleId), ForVehicle(vehicleId)], NewClock(), OtherActor);

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
            NewRate(), [badRate], NewClock(), OtherActor);

        result.Error.ShouldBe(ContractErrors.MinimumNotApplicableByTrip);
        contract.PricingMode.ShouldBe(PricingMode.ByHour);
        contract.VehicleRates.ShouldBe([original]);
        contract.Version.ShouldBe(0);
        contract.DomainEvents.ShouldBeEmpty();
    }
}
