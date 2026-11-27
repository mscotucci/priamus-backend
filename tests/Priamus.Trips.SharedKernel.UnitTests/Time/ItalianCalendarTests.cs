using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.SharedKernel.UnitTests.Time;

public sealed class ItalianCalendarTests
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;

        public DateTimeOffset UtcNowOffset => new(utcNow, TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-11-18T23:30:00Z", "2026-11-19")] // winter time, UTC+1: already the next day
    [InlineData("2026-11-18T22:59:00Z", "2026-11-18")] // winter time, one minute before midnight in Rome
    [InlineData("2026-07-01T21:59:00Z", "2026-07-01")] // summer time, UTC+2: still the same day
    [InlineData("2026-07-01T22:00:00Z", "2026-07-02")] // summer time, midnight in Rome
    public void TodayInItaly_ReturnsTheRomeDate(string utcInstant, string expectedDay)
    {
        var utc = DateTime.Parse(utcInstant, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        new FixedClock(utc).TodayInItaly().ShouldBe(DateOnly.Parse(expectedDay));
    }

    [Theory]
    [InlineData("2026-11-18T23:30:00Z", "2026-11-19T00:30:00")] // winter time, UTC+1
    [InlineData("2026-07-01T21:59:00Z", "2026-07-01T23:59:00")] // summer time, UTC+2
    public void NowInItaly_ReturnsTheRomeWallClock(string utcInstant, string expectedNow)
    {
        var utc = DateTime.Parse(utcInstant, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        new FixedClock(utc).NowInItaly().ShouldBe(DateTime.Parse(expectedNow, System.Globalization.CultureInfo.InvariantCulture));
    }
}
