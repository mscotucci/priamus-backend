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
}
