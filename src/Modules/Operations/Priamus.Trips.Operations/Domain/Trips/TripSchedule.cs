namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>
/// When a trip happened, on the wall clock of Italy: the half-open span [start, end). A trip in progress
/// has no end and is open until now. An end time not after the start time is on the next day.
/// </summary>
/// <param name="StartsAt">The instant the trip starts.</param>
/// <param name="EndsAt">The instant the trip ends; <c>null</c> for a trip in progress.</param>
public readonly record struct TripSchedule(DateTime StartsAt, DateTime? EndsAt)
{
    /// <summary>Builds the span of a trip from its day and times; an end not after the start is the day after.</summary>
    /// <param name="day">The trip day.</param>
    /// <param name="start">The start time.</param>
    /// <param name="end">The end time; <c>null</c> for a trip in progress.</param>
    public static TripSchedule Of(DateOnly day, TimeOnly start, TimeOnly? end)
    {
        var startsAt = day.ToDateTime(start);
        if (end is not { } endTime)
        {
            return new TripSchedule(startsAt, null);
        }

        var endsAt = day.ToDateTime(endTime);
        return new TripSchedule(startsAt, endTime <= start ? endsAt.AddDays(1) : endsAt);
    }

    /// <summary>
    /// Tells whether <paramref name="moment"/> is later than <paramref name="nowInItaly"/>; there is no
    /// tolerance (a trip is typed after it happened).
    /// </summary>
    /// <param name="moment">The start or end instant typed by the driver.</param>
    /// <param name="nowInItaly">The present moment on the wall clock of Italy.</param>
    public static bool IsInTheFuture(DateTime moment, DateTime nowInItaly) => moment > nowInItaly;

    /// <summary>
    /// Finds the first of <paramref name="others"/>, by start, that is in the way of a trip starting at
    /// <paramref name="start"/>: the start falls inside the other span (a trip in progress is open until
    /// <paramref name="nowInItaly"/>), or the other trip starts at or after it, so the new trip would be
    /// open across it. A start exactly at the end of the other is not in the way.
    /// </summary>
    /// <param name="start">The start typed by the driver.</param>
    /// <param name="others">The other started trips of the same driver.</param>
    /// <param name="nowInItaly">The present moment on the wall clock of Italy.</param>
    /// <returns>The trip in the way, or <c>null</c> when there is none.</returns>
    public static TripSchedule? FindOverlapOfStart(
        DateTime start, IEnumerable<TripSchedule> others, DateTime nowInItaly)
    {
        ArgumentNullException.ThrowIfNull(others);

        foreach (var other in others.OrderBy(o => o.StartsAt))
        {
            var startsInOther = other.StartsAt <= start && start < (other.EndsAt ?? nowInItaly);
            if (startsInOther || other.StartsAt >= start)
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first of <paramref name="others"/>, by start, whose start is inside the span
    /// [<paramref name="start"/>, <paramref name="end"/>) a trip would close. An end exactly at the start
    /// of the other is not in the way.
    /// </summary>
    /// <param name="start">The start of the trip being ended.</param>
    /// <param name="end">The end typed by the driver.</param>
    /// <param name="others">The other started trips of the same driver.</param>
    /// <returns>The trip in the way, or <c>null</c> when there is none.</returns>
    public static TripSchedule? FindOverlapOfEnd(
        DateTime start, DateTime end, IEnumerable<TripSchedule> others)
    {
        ArgumentNullException.ThrowIfNull(others);

        foreach (var other in others.OrderBy(o => o.StartsAt))
        {
            if (start <= other.StartsAt && other.StartsAt < end)
            {
                return other;
            }
        }

        return null;
    }
}
