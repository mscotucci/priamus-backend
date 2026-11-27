namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>Where a trip is, read from its times alone: it is never stored.</summary>
public enum TripState
{
    /// <summary>Added and not started: it has no start time.</summary>
    Planned,

    /// <summary>Started and not ended: it has a start time and no end time.</summary>
    InProgress,

    /// <summary>Ended: it has a start time and an end time.</summary>
    Ended,
}
