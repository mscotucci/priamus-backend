namespace Priamus.Trips.Registry.Contracts;

/// <summary>What a trip must record about its size.</summary>
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
