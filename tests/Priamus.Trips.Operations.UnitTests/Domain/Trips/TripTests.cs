using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.UnitTests.Domain.Trips;

public sealed class TripTests
{
    private static readonly Guid DriverId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid PickupId = Guid.NewGuid();
    private static readonly Guid DeliveryId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 24);
    private static readonly DateTime AddedAt = new(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndedAt = new(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
    private static readonly UserId User = new(Guid.NewGuid());

    private static RegistryContracts.TripPricing Pricing() => new(
        RegistryContracts.TripRateSource.LocationPair,
        VehicleId,
        PickupId,
        DeliveryId,
        DayPrice: 50m,
        NightPrice: 60m,
        NightCallFee: 15m,
        RegistryContracts.TripMinimumKind.TieredOverage,
        MinimumAmount: 200m,
        MinimumThreshold: 4m,
        MinimumOveragePrice: 45m,
        NightBandStart: new TimeOnly(22, 0),
        NightBandEnd: new TimeOnly(6, 0),
        NightSurcharge: true,
        IsNight: true,
        Quantity: 300,
        UnitPrice: 60m,
        BaseAmount: 300m,
        AmountAfterMinimum: 310m,
        MinimumApplied: true,
        AppliedNightCallFee: 15m,
        Total: 325m);

    private static RegistryContracts.PricedTrip Priced(RegistryContracts.TripMeasureKind measure) =>
        new(Guid.NewGuid(), measure, ["DeliveryNote", "WeighingTicket"], Pricing());

    private static Trip Planned(DateOnly? addedOn = null) =>
        Trip.Add(TripId.New(), DriverId, CustomerId, VehicleId, null, PickupId, DeliveryId, AddedAt, addedOn ?? Today, User);

    private static Trip InProgress(TimeOnly? start = null)
    {
        var trip = Planned();
        trip.StartAt(Today, start ?? new TimeOnly(8, 0)).IsSuccess.ShouldBeTrue();
        return trip;
    }

    private static Result End(
        Trip trip,
        TimeOnly end,
        RegistryContracts.TripMeasureKind measure = RegistryContracts.TripMeasureKind.StartOnly,
        int? kilograms = null,
        int? kilometres = null) =>
        trip.EndAt(end, kilograms, kilometres, Priced(measure), number: 42, EndedAt);

    // ─── State ────────────────────────────────────────────────────────────

    [Fact]
    public void State_IsReadFromTheTimesAlone()
    {
        var trip = Planned();
        trip.State.ShouldBe(TripState.Planned);
        trip.Day.ShouldBeNull();
        trip.Start.ShouldBeNull();
        trip.End.ShouldBeNull();

        trip.StartAt(Today, new TimeOnly(8, 0)).IsSuccess.ShouldBeTrue();
        trip.State.ShouldBe(TripState.InProgress);
        trip.Start.ShouldBe(new TimeOnly(8, 0));
        trip.End.ShouldBeNull();

        End(trip, new TimeOnly(10, 0)).IsSuccess.ShouldBeTrue();
        trip.State.ShouldBe(TripState.Ended);
    }

    [Fact]
    public void Add_BuildsAPlannedTripWithNothingPriced()
    {
        var trip = Planned();

        trip.DriverId.ShouldBe(DriverId);
        trip.CustomerId.ShouldBe(CustomerId);
        trip.LeadingVehicleId.ShouldBe(VehicleId);
        trip.TrailerId.ShouldBeNull();
        trip.PickupLocationId.ShouldBe(PickupId);
        trip.DeliveryLocationId.ShouldBe(DeliveryId);
        trip.AddedAt.ShouldBe(AddedAt);
        trip.AddedOn.ShouldBe(Today);
        trip.AddedBy.ShouldBe(User);
        trip.Pricing.ShouldBeNull();
        trip.ContractId.ShouldBeNull();
        trip.MeasureKind.ShouldBeNull();
        trip.Number.ShouldBeNull();
        trip.EndedAt.ShouldBeNull();
        trip.RequiredDocuments.ShouldBeEmpty();
        trip.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void StartAt_ATripThatIsNotPlanned_IsRefusedAndChangesNothing()
    {
        var trip = InProgress(new TimeOnly(8, 0));

        var result = trip.StartAt(Today, new TimeOnly(9, 0));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Trip.NotPlanned");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        trip.Start.ShouldBe(new TimeOnly(8, 0));
    }

    [Fact]
    public void EndAt_ATripThatIsNotInProgress_IsRefused()
    {
        var planned = End(Planned(), new TimeOnly(10, 0));
        planned.IsFailure.ShouldBeTrue();
        planned.Error.Code.ShouldBe("Operations.Trip.NotInProgress");
        planned.Error.Type.ShouldBe(ErrorType.Conflict);

        var ended = InProgress();
        End(ended, new TimeOnly(10, 0)).IsSuccess.ShouldBeTrue();
        var again = End(ended, new TimeOnly(11, 0));
        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("Operations.Trip.NotInProgress");
        ended.End.ShouldBe(new TimeOnly(10, 0));
    }

    // ─── What the end keeps ───────────────────────────────────────────────

    [Fact]
    public void EndAt_KeepsWhatTheTripWasPricedWith()
    {
        var priced = Priced(RegistryContracts.TripMeasureKind.StartAndEnd);
        var trip = InProgress();

        var result = trip.EndAt(new TimeOnly(12, 0), null, null, priced, number: 7, EndedAt);

        result.IsSuccess.ShouldBeTrue();
        trip.ContractId.ShouldBe(priced.ContractId);
        trip.MeasureKind.ShouldBe(TripMeasureKind.StartAndEnd);
        trip.RequiredDocuments.ShouldBe(["DeliveryNote", "WeighingTicket"]);
        trip.Number.ShouldBe(7);
        trip.EndedAt.ShouldBe(EndedAt);
        trip.Pricing.ShouldBe(new TripPricing(
            TripRateSource.LocationPair,
            VehicleId,
            PickupId,
            DeliveryId,
            50m,
            60m,
            15m,
            TripMinimumKind.TieredOverage,
            200m,
            4m,
            45m,
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            true,
            true,
            300,
            60m,
            300m,
            310m,
            true,
            15m,
            325m));
    }

    [Fact]
    public void Trip_OffersNoWayToChangeWhatItKeeps()
    {
        var withPublicSetter = typeof(Trip).GetProperties()
            .Concat(typeof(TripPricing).GetProperties())
            .Where(p => p.SetMethod is { IsPublic: true } set
                && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"));

        withPublicSetter.ShouldBeEmpty();

        var documents = new List<string> { "DeliveryNote" };
        var priced = new RegistryContracts.PricedTrip(Guid.NewGuid(), RegistryContracts.TripMeasureKind.StartOnly, documents, Pricing());
        var trip = InProgress();
        trip.EndAt(new TimeOnly(10, 0), null, null, priced, 1, EndedAt).IsSuccess.ShouldBeTrue();
        documents.Add("Other");

        trip.RequiredDocuments.ShouldBe(["DeliveryNote"]);
    }

    [Theory]
    [InlineData(RegistryContracts.TripMeasureKind.StartAndEnd, null, null)]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, 18_500, null)]
    [InlineData(RegistryContracts.TripMeasureKind.Distance, null, 240)]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, null, null)]
    public void EndAt_WhateverTheMeasureKind_KeepsTheEndTimeAndTheMeasureTheKindAsks(
        RegistryContracts.TripMeasureKind measure, int? kilograms, int? kilometres)
    {
        var trip = InProgress(new TimeOnly(8, 0));

        var result = End(trip, new TimeOnly(11, 30), measure, kilograms, kilometres);

        result.IsSuccess.ShouldBeTrue();
        trip.End.ShouldBe(new TimeOnly(11, 30));
        trip.State.ShouldBe(TripState.Ended);
        trip.MeasureKind!.Value.ToString().ShouldBe(measure.ToString());
        trip.Kilograms.ShouldBe(kilograms);
        trip.Kilometres.ShouldBe(kilometres);
    }

    [Theory]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, false, false, "Operations.Trip.KilogramsMismatch")]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, true, false, "Operations.Trip.KilogramsMismatch")]
    [InlineData(RegistryContracts.TripMeasureKind.StartAndEnd, true, false, "Operations.Trip.KilogramsMismatch")]
    [InlineData(RegistryContracts.TripMeasureKind.Distance, false, false, "Operations.Trip.KilometresMismatch")]
    [InlineData(RegistryContracts.TripMeasureKind.StartOnly, false, true, "Operations.Trip.KilometresMismatch")]
    [InlineData(RegistryContracts.TripMeasureKind.Weight, true, true, "Operations.Trip.KilometresMismatch")]
    public void EndAt_MeasureValuesNotMatchingTheMeasureKind_AreRefusedAndTheTripStaysInProgress(
        RegistryContracts.TripMeasureKind measure, bool withKilograms, bool withKilometres, string code)
    {
        var trip = InProgress();

        var result = End(trip, new TimeOnly(10, 0), measure, withKilograms ? 100 : null, withKilometres ? 100 : null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        trip.State.ShouldBe(TripState.InProgress);
        trip.End.ShouldBeNull();
        trip.Pricing.ShouldBeNull();
    }

    // ─── Duration ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(8, 1, true)]      // one minute
    [InlineData(23, 59, true)]    // 15 hours 59 minutes, the same day
    [InlineData(7, 59, true)]     // 23 hours 59 minutes = 1439, the day after
    [InlineData(8, 0, false)]     // 24 hours: not allowed
    public void EndAt_ATripLastsOneToThe1439Minutes(int hour, int minute, bool accepted)
    {
        var trip = InProgress(new TimeOnly(8, 0));

        var result = End(trip, new TimeOnly(hour, minute));

        result.IsSuccess.ShouldBe(accepted);
        if (!accepted)
        {
            result.Error.Code.ShouldBe("Operations.Trip.DurationOutOfRange");
            result.Error.Type.ShouldBe(ErrorType.Conflict);
            trip.State.ShouldBe(TripState.InProgress);
        }
    }

    [Fact]
    public void EndAt_AnEndTimeBeforeTheStart_IsTheDayAfter()
    {
        var trip = InProgress(new TimeOnly(22, 0));

        End(trip, new TimeOnly(6, 0)).IsSuccess.ShouldBeTrue();

        trip.End.ShouldBe(new TimeOnly(6, 0));
        trip.State.ShouldBe(TripState.Ended);
    }

    // ─── Expiry ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, false)]  // the day it was added
    [InlineData(7, false)]  // the seventh day after
    [InlineData(8, true)]   // from the eighth day after
    [InlineData(30, true)]
    public void IsExpired_FromTheEighthDayAfterTheDayItWasAdded(int daysAfter, bool expired)
    {
        Trip.IsExpired(Today, Today.AddDays(daysAfter)).ShouldBe(expired);
        Planned().IsExpiredOn(Today.AddDays(daysAfter)).ShouldBe(expired);
    }

    [Theory]
    [InlineData(0, 7)]  // the day it was added
    [InlineData(1, 6)]
    [InlineData(7, 0)]  // the last day
    [InlineData(8, -1)] // expired
    public void DaysToExpiry_CountsTheDaysLeftFromTheDayItWasAdded(int daysAfter, int expected)
    {
        Planned().DaysToExpiry(Today.AddDays(daysAfter)).ShouldBe(expected);
    }

    [Fact]
    public void IsExpiredOn_ATripThatStarted_NeverExpires()
    {
        var trip = InProgress();

        trip.IsExpiredOn(Today.AddDays(30)).ShouldBeFalse();
    }

    // ─── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public void Delete_APlannedTrip_IsMarkedWithWhoAndWhenAndStaysThere()
    {
        var trip = Planned();
        var at = new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc);
        var by = new UserId(Guid.NewGuid());

        var result = trip.Delete(at, by);

        result.IsSuccess.ShouldBeTrue();
        trip.DeletedAt.ShouldBe(at);
        trip.DeletedBy.ShouldBe(by);
        trip.State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public void Delete_ATripInProgress_IsMarkedWithWhoAndWhen()
    {
        var trip = InProgress();
        var at = new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc);

        trip.Delete(at, User).IsSuccess.ShouldBeTrue();

        trip.DeletedAt.ShouldBe(at);
        trip.DeletedBy.ShouldBe(User);
    }

    [Fact]
    public void Delete_AnEndedTrip_IsRefusedAndLeavesNoMark()
    {
        var trip = InProgress();
        End(trip, new TimeOnly(10, 0)).IsSuccess.ShouldBeTrue();

        var result = trip.Delete(new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc), User);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Trip.EndedTripCannotBeDeleted");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        trip.DeletedAt.ShouldBeNull();
        trip.DeletedBy.ShouldBeNull();
    }

    [Fact]
    public void Version_IsZeroWhenAddedAndGrowsByOneWithEachActThatUpdatesTheTrip()
    {
        var trip = Planned();
        trip.Version.ShouldBe(0);

        trip.StartAt(Today, new TimeOnly(8, 0)).IsSuccess.ShouldBeTrue();
        trip.Version.ShouldBe(1);

        End(trip, new TimeOnly(10, 0)).IsSuccess.ShouldBeTrue();
        trip.Version.ShouldBe(2);
    }

    [Fact]
    public void Delete_GrowsTheVersionByOne()
    {
        var trip = Planned();

        trip.Delete(new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc), User).IsSuccess.ShouldBeTrue();

        trip.Version.ShouldBe(1);
    }

    [Fact]
    public void ARefusedAct_LeavesTheVersionAsItWas()
    {
        var ended = InProgress();
        End(ended, new TimeOnly(10, 0)).IsSuccess.ShouldBeTrue();
        var versionEnded = ended.Version;
        var planned = Planned();

        ended.StartAt(Today, new TimeOnly(11, 0)).IsFailure.ShouldBeTrue();
        End(ended, new TimeOnly(11, 0)).IsFailure.ShouldBeTrue();
        ended.Delete(new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc), User).IsFailure.ShouldBeTrue();
        End(planned, new TimeOnly(11, 0)).IsFailure.ShouldBeTrue();

        ended.Version.ShouldBe(versionEnded);
        planned.Version.ShouldBe(0);
    }
}
