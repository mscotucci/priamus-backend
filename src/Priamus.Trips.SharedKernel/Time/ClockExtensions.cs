namespace Priamus.Trips.SharedKernel.Time;

/// <summary>Calendar helpers over <see cref="IClock"/>.</summary>
public static class ClockExtensions
{
    private const string ItalianTimeZoneId = "Europe/Rome";

    /// <summary>The current calendar day in Italian time (Europe/Rome, summer time included).</summary>
    /// <param name="clock">Time source.</param>
    public static DateOnly TodayInItaly(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(ItalianTimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, zone));
    }

    /// <summary>The present moment on the wall clock of Italy (Europe/Rome, summer time included).</summary>
    /// <param name="clock">Time source.</param>
    public static DateTime NowInItaly(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(ItalianTimeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, zone);
    }

    /// <summary>The present moment on the wall clock of Italy, cut to the minute: a trip is stored by the minute.</summary>
    /// <param name="clock">Time source.</param>
    public static DateTime NowInItalyToTheMinute(this IClock clock)
    {
        var now = clock.NowInItaly();
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
    }
}
