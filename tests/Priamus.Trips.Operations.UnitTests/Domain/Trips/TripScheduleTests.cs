using Priamus.Trips.Operations.Domain.Trips;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.UnitTests.Domain.Trips;

public sealed class TripScheduleTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);
    private static readonly DateTime Now = new(2026, 11, 24, 10, 0, 0);

    private static TimeOnly At(int hour, int minute = 0) => new(hour, minute);

    private static DateTime Moment(int hour, int minute = 0) => Today.ToDateTime(At(hour, minute));

    private static TripSchedule Ended(DateOnly day, TimeOnly start, TimeOnly end) => TripSchedule.Of(day, start, end);

    private static TripSchedule Open(DateOnly day, TimeOnly start) => TripSchedule.Of(day, start, null);

    // ─── Span ─────────────────────────────────────────────────────────────

    [Fact]
    public void Of_AnEndNotAfterTheStart_IsTheDayAfter()
    {
        Ended(Today, At(22), At(6)).EndsAt.ShouldBe(new DateTime(2026, 11, 25, 6, 0, 0));
        Ended(Today, At(8), At(8)).EndsAt.ShouldBe(new DateTime(2026, 11, 25, 8, 0, 0));
        Ended(Today, At(8), At(10)).EndsAt.ShouldBe(Moment(10));
    }

    [Fact]
    public void Of_ATripInProgress_HasNoEnd()
    {
        Open(Today, At(8)).EndsAt.ShouldBeNull();
        Open(Today, At(8)).StartsAt.ShouldBe(Moment(8));
    }

    // ─── Future ───────────────────────────────────────────────────────────

    [Fact]
    public void IsInTheFuture_AMomentAfterNow_IsInTheFuture_WithNoTolerance()
    {
        TripSchedule.IsInTheFuture(Moment(10, 1), Now).ShouldBeTrue();
        TripSchedule.IsInTheFuture(Today.AddDays(1).ToDateTime(At(0, 1)), Now).ShouldBeTrue();
    }

    [Fact]
    public void IsInTheFuture_NowOrBefore_IsNotInTheFuture()
    {
        TripSchedule.IsInTheFuture(Moment(10), Now).ShouldBeFalse();
        TripSchedule.IsInTheFuture(Moment(9, 59), Now).ShouldBeFalse();
        TripSchedule.IsInTheFuture(Today.AddDays(-1).ToDateTime(At(23, 59)), Now).ShouldBeFalse();
    }

    // ─── Overlap of a start ───────────────────────────────────────────────

    [Fact]
    public void FindOverlapOfStart_AStartInsideAnotherSpan_ReturnsThatTrip()
    {
        var other = Ended(Today, At(6), At(9));

        TripSchedule.FindOverlapOfStart(Moment(8), [other], Now).ShouldBe(other);
        TripSchedule.FindOverlapOfStart(Moment(6), [other], Now).ShouldBe(other);
    }

    [Fact]
    public void FindOverlapOfStart_AStartExactlyAtTheEndOfAnother_IsAllowed()
    {
        var other = Ended(Today, At(6), At(9));

        TripSchedule.FindOverlapOfStart(Moment(9), [other], Now).ShouldBeNull();
        TripSchedule.FindOverlapOfStart(Moment(9, 30), [other], Now).ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfStart_AnotherTripStartingAfterIt_ReturnsThatTrip()
    {
        var later = Ended(Today, At(9), At(9, 30));

        TripSchedule.FindOverlapOfStart(Moment(8), [later], Now).ShouldBe(later);
    }

    [Fact]
    public void FindOverlapOfStart_AStartInsideATripThatEndedTheNextDay_ReturnsThatTrip()
    {
        var yesterdayNight = Ended(Today.AddDays(-1), At(22), At(6));

        TripSchedule.FindOverlapOfStart(Moment(5), [yesterdayNight], Now).ShouldBe(yesterdayNight);
        TripSchedule.FindOverlapOfStart(Moment(6), [yesterdayNight], Now).ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfStart_ATripInProgressIsOpenUntilNow()
    {
        var inProgress = Open(Today, At(8));

        TripSchedule.FindOverlapOfStart(Moment(9), [inProgress], Now).ShouldBe(inProgress);
        TripSchedule.FindOverlapOfStart(Moment(7), [inProgress], Now).ShouldBe(inProgress);
        TripSchedule.FindOverlapOfStart(Moment(10), [inProgress], Now).ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfStart_NothingInTheWay_ReturnsNull()
    {
        var before = Ended(Today.AddDays(-2), At(8), At(12));

        TripSchedule.FindOverlapOfStart(Moment(8), [before], Now).ShouldBeNull();
        TripSchedule.FindOverlapOfStart(Moment(8), [], Now).ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfStart_SeveralInTheWay_ReturnsTheEarliest()
    {
        var first = Ended(Today, At(7), At(9));
        var second = Ended(Today, At(8), At(12));

        TripSchedule.FindOverlapOfStart(Moment(8, 30), [second, first], Now).ShouldBe(first);
        TripSchedule.FindOverlapOfStart(Moment(6), [second, first], Now).ShouldBe(first);
    }

    // ─── Overlap of an end ────────────────────────────────────────────────

    [Fact]
    public void FindOverlapOfEnd_ASpanContainingAnotherStart_ReturnsThatTrip()
    {
        var other = Open(Today, At(10));

        TripSchedule.FindOverlapOfEnd(Moment(8), Moment(12), [other]).ShouldBe(other);
        TripSchedule.FindOverlapOfEnd(Moment(10), Moment(12), [other]).ShouldBe(other);
    }

    [Fact]
    public void FindOverlapOfEnd_AnEndExactlyAtTheStartOfAnother_IsAllowed()
    {
        var other = Open(Today, At(12));

        TripSchedule.FindOverlapOfEnd(Moment(10), Moment(12), [other]).ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfEnd_AnEndTheDayAfter_ReachesTheTripsOfThatDay()
    {
        var tomorrowMorning = Open(Today.AddDays(1), At(5));

        TripSchedule.FindOverlapOfEnd(Moment(22), new DateTime(2026, 11, 25, 6, 0, 0), [tomorrowMorning])
            .ShouldBe(tomorrowMorning);
        TripSchedule.FindOverlapOfEnd(Moment(22), new DateTime(2026, 11, 25, 5, 0, 0), [tomorrowMorning])
            .ShouldBeNull();
    }

    [Fact]
    public void FindOverlapOfEnd_TripsBeforeTheStart_AreNotInTheWay()
    {
        var before = Ended(Today, At(5), At(7));

        TripSchedule.FindOverlapOfEnd(Moment(8), Moment(12), [before]).ShouldBeNull();
    }
}
