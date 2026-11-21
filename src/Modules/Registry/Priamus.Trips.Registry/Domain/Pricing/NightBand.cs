using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>
/// The interval of the day in which a trip, by its start time, is a night trip (PRD §5.8): the
/// start is included, the end excluded, and the band may wrap midnight. Both times are whole minutes.
/// </summary>
public sealed record NightBand
{
    private NightBand(TimeOnly start, TimeOnly end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The band nobody has changed: 22:00–06:00.</summary>
    public static NightBand Default { get; } = new(new TimeOnly(22, 0), new TimeOnly(6, 0));

    /// <summary>First minute of the band.</summary>
    public TimeOnly Start { get; }

    /// <summary>First minute after the band.</summary>
    public TimeOnly End { get; }

    /// <summary>Builds a band, dropping seconds and fractions of the times.</summary>
    /// <param name="start">First minute of the band.</param>
    /// <param name="end">First minute after the band; not the same minute as <paramref name="start"/>.</param>
    /// <returns>A failure when the end equals the start.</returns>
    public static Result<NightBand> Create(TimeOnly start, TimeOnly end)
    {
        var wholeStart = new TimeOnly(start.Hour, start.Minute);
        var wholeEnd = new TimeOnly(end.Hour, end.Minute);
        return wholeStart == wholeEnd
            ? PricingErrors.NightBandEndEqualsStart
            : new NightBand(wholeStart, wholeEnd);
    }

    /// <summary>Whether a trip starting at <paramref name="start"/> is a night trip.</summary>
    public bool IsNight(TimeOnly start) =>
        Start < End
            ? start >= Start && start < End
            : start >= Start || start < End;
}
