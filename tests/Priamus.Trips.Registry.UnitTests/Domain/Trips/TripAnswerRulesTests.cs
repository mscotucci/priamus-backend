using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Trips;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Trips;

public sealed class TripAnswerRulesTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly FakeClock Clock = new(new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc));

    private static readonly LocationId Pescara = LocationId.New();
    private static readonly LocationId Chieti = LocationId.New();
    private static readonly LocationId Teramo = LocationId.New();
    private static readonly Rate DayOnly = Rate.Create(45m, null, null, new MinimumRule.None()).Value;

    private static Vehicle NewVehicle(
        VehicleType type = VehicleType.Tractor, bool requiresTrailer = false, bool decommissioned = false)
    {
        var vehicle = Vehicle.Create(
            VehicleId.New(), LicensePlate.Create("AB456CD"), "Iveco Stralis 480", type, requiresTrailer, Clock, Actor);
        if (decommissioned)
        {
            vehicle.Decommission(Clock, Actor);
        }

        return vehicle;
    }

    private static LocationRate RateOf(LocationId? pickup, LocationId? delivery) => new(pickup, delivery, DayOnly);

    private static HashSet<LocationId> Active(params LocationId[] ids) => [.. ids];

    // ── Leading vehicle ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(VehicleType.Tractor, false, true)]
    [InlineData(VehicleType.StandaloneVehicle, false, true)]
    [InlineData(VehicleType.Trailer, false, false)]
    [InlineData(VehicleType.Tractor, true, false)]
    public void CanLead_AcceptsActiveTractorsAndStandaloneVehicles(VehicleType type, bool decommissioned, bool expected)
    {
        var vehicle = NewVehicle(type, decommissioned: decommissioned);

        TripAnswerRules.CanLead(vehicle).ShouldBe(expected);
    }

    // ── Trailer ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VehicleType.Tractor, VehicleType.Trailer, false, true)]
    [InlineData(VehicleType.StandaloneVehicle, VehicleType.Trailer, false, false)]
    [InlineData(VehicleType.Tractor, VehicleType.Trailer, true, false)]
    [InlineData(VehicleType.Tractor, VehicleType.Tractor, false, false)]
    public void TrailerHolds_NeedsAnActiveTrailerBehindATractor(
        VehicleType leadingType, VehicleType trailerType, bool trailerDecommissioned, bool expected)
    {
        var leading = NewVehicle(leadingType);
        var trailer = NewVehicle(trailerType, decommissioned: trailerDecommissioned);

        TripAnswerRules.TrailerHolds(leading, trailer).ShouldBe(expected);
    }

    [Fact]
    public void TrailerHolds_WithoutALeadingVehicle_JudgesTheTrailerAlone()
    {
        TripAnswerRules.TrailerHolds(null, NewVehicle(VehicleType.Trailer)).ShouldBeTrue();
        TripAnswerRules.TrailerHolds(null, NewVehicle(VehicleType.Trailer, decommissioned: true)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void TrailerMissing_IsTrueWhenTheLeadingVehicleRequiresOneAndNoneIsNamed(
        bool requiresTrailer, bool trailerGiven, bool expected)
    {
        var leading = NewVehicle(requiresTrailer: requiresTrailer);

        TripAnswerRules.TrailerMissing(leading, trailerGiven).ShouldBe(expected);
    }

    // ── Locations ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BillingLevel.FlatRate, false, false, false)]
    [InlineData(BillingLevel.PerPickupLocation, false, true, true)]
    [InlineData(BillingLevel.PerPickupLocation, true, false, false)]
    [InlineData(BillingLevel.PerDeliveryLocation, true, false, true)]
    [InlineData(BillingLevel.PerDeliveryLocation, false, true, false)]
    [InlineData(BillingLevel.PerLocationPair, true, true, false)]
    [InlineData(BillingLevel.PerLocationPair, true, false, true)]
    [InlineData(BillingLevel.PerLocationPair, false, true, true)]
    public void LocationMissing_IsTrueWhenTheLevelNamesALocationNotGiven(
        BillingLevel level, bool pickupGiven, bool deliveryGiven, bool missing)
    {
        LocationId? pickup = pickupGiven ? Pescara : null;
        LocationId? delivery = deliveryGiven ? Chieti : null;

        TripAnswerRules.LocationMissing(level, pickup, delivery).ShouldBe(missing);
    }

    [Fact]
    public void LocationsHold_OnAFlatRateContract_AcceptsAnyActiveLocationAndRefusesAnInactiveOne()
    {
        var holds = TripAnswerRules.LocationsHold(BillingLevel.FlatRate, [], Pescara, Chieti, Active(Pescara));

        holds.ShouldBe((true, false));
    }

    [Fact]
    public void LocationsHold_OnAPickupContract_NeedsTheRatedPickupAndAcceptsAnyActiveDelivery()
    {
        var rates = new[] { RateOf(Pescara, null) };

        TripAnswerRules.LocationsHold(BillingLevel.PerPickupLocation, rates, Pescara, Teramo, Active(Pescara, Teramo))
            .ShouldBe((true, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerPickupLocation, rates, Chieti, Teramo, Active(Chieti, Teramo))
            .ShouldBe((false, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerPickupLocation, rates, Pescara, Teramo, Active(Teramo))
            .ShouldBe((false, true));
    }

    [Fact]
    public void LocationsHold_OnADeliveryContract_NeedsTheRatedDeliveryAndAcceptsAnyActivePickup()
    {
        var rates = new[] { RateOf(null, Chieti) };

        TripAnswerRules.LocationsHold(BillingLevel.PerDeliveryLocation, rates, Teramo, Chieti, Active(Teramo, Chieti))
            .ShouldBe((true, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerDeliveryLocation, rates, Teramo, Pescara, Active(Teramo, Pescara))
            .ShouldBe((true, false));
    }

    [Fact]
    public void LocationsHold_OnAPairContract_JudgesThePairTogether()
    {
        var rates = new[] { RateOf(Pescara, Chieti) };
        var all = Active(Pescara, Chieti, Teramo);

        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, Pescara, Chieti, all).ShouldBe((true, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, Pescara, Teramo, all).ShouldBe((false, false));
        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, Pescara, Chieti, Active(Pescara))
            .ShouldBe((false, false));
    }

    [Fact]
    public void LocationsHold_OnAPairContract_AcceptsALoneSideSomeRatedPairHas_AndLeavesAbsentSidesTrue()
    {
        var rates = new[] { RateOf(Pescara, Chieti) };
        var all = Active(Pescara, Chieti, Teramo);

        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, Pescara, null, all).ShouldBe((true, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, Teramo, null, all).ShouldBe((false, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, null, Chieti, all).ShouldBe((true, true));
        TripAnswerRules.LocationsHold(BillingLevel.PerLocationPair, rates, null, null, all).ShouldBe((true, true));
    }
}
