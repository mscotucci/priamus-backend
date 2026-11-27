namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>What a trip records about its size, as its contract's pricing mode asked.</summary>
public enum TripMeasureKind
{
    /// <summary>The start time and the end time.</summary>
    StartAndEnd,

    /// <summary>The start time and the kilograms carried.</summary>
    Weight,

    /// <summary>The start time and the kilometres driven.</summary>
    Distance,

    /// <summary>The start time alone.</summary>
    StartOnly,
}
