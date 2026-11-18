using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Contracts;

public sealed class ValidityTests
{
    private static Validity V(string from, string? to) =>
        Validity.Create(DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to)).Value;

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithEndBeforeStart_FailsWithEndBeforeStartError()
    {
        var result = Validity.Create(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 9));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ContractErrors.ValidityEndBeforeStart);
    }

    [Fact]
    public void Create_WithSameDayStartAndEnd_Succeeds()
    {
        var result = Validity.Create(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 10));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ValidFrom.ShouldBe(new DateOnly(2026, 5, 10));
        result.Value.ValidTo.ShouldBe(new DateOnly(2026, 5, 10));
    }

    [Fact]
    public void Create_WithoutEnd_IsOpen()
    {
        var validity = V("2026-05-10", null);

        validity.ValidTo.ShouldBeNull();
        validity.IsOpen.ShouldBeTrue();
    }

    // ── Overlaps ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-31", "2026-02-28", true)]   // share one day
    [InlineData("2026-01-01", "2026-01-31", "2026-02-01", "2026-02-28", false)]  // adjacent: end D, start D+1
    [InlineData("2026-02-01", "2026-02-28", "2026-01-01", "2026-01-31", false)]  // adjacent, reversed
    [InlineData("2026-01-01", "2026-01-31", "2026-03-01", "2026-03-31", false)]  // disjoint
    [InlineData("2026-01-01", "2026-01-31", "2026-01-10", "2026-01-20", true)]   // contained
    [InlineData("2026-01-01", null, "2030-12-01", "2030-12-31", true)]           // open extends without end
    [InlineData("2026-06-01", null, "2026-01-01", "2026-05-31", false)]          // open starts the day after the other ends
    [InlineData("2026-06-01", null, "2027-07-01", null, true)]                   // two open
    public void Overlaps_IsTrueExactlyWhenTheValiditiesShareADay(
        string aFrom, string? aTo, string bFrom, string? bTo, bool expected)
    {
        var a = V(aFrom, aTo);
        var b = V(bFrom, bTo);

        a.Overlaps(b).ShouldBe(expected);
        b.Overlaps(a).ShouldBe(expected);
    }

    // ── StatusOn ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-04-01", "2026-12-31", "2026-04-01", ContractStatus.Current)]   // first day
    [InlineData("2026-04-01", "2026-12-31", "2026-12-31", ContractStatus.Current)]   // last day
    [InlineData("2026-04-01", "2026-12-31", "2026-03-31", ContractStatus.Upcoming)]  // day before the start
    [InlineData("2026-04-01", "2026-12-31", "2027-01-01", ContractStatus.Expired)]   // day after the end
    [InlineData("2026-04-01", null, "2035-01-01", ContractStatus.Current)]           // open, long after the start
    [InlineData("2026-04-01", null, "2026-03-31", ContractStatus.Upcoming)]          // open, before the start
    public void StatusOn_ReadsTheDayAgainstTheValidity(
        string from, string? to, string today, ContractStatus expected)
    {
        V(from, to).StatusOn(DateOnly.Parse(today)).ShouldBe(expected);
    }

    // ── CloseOn ──────────────────────────────────────────────────────────────

    [Fact]
    public void CloseOn_OpenValidity_ReturnsValidityEndingOnThatDay()
    {
        var closed = V("2026-04-01", null).CloseOn(new DateOnly(2026, 6, 30));

        closed.IsSuccess.ShouldBeTrue();
        closed.Value.ValidFrom.ShouldBe(new DateOnly(2026, 4, 1));
        closed.Value.ValidTo.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void CloseOn_BeforeStart_FailsWithEndBeforeStartError()
    {
        var closed = V("2026-04-01", null).CloseOn(new DateOnly(2026, 3, 31));

        closed.Error.ShouldBe(ContractErrors.ValidityEndBeforeStart);
    }
}
