namespace Priamus.Trips.Operations.Features;

/// <summary>
/// What the end of a trip still asks of the driver. It names the measure, never the way the contract prices.
/// </summary>
public enum TripEndAsks
{
    /// <summary>Nothing: the trip is ended at once.</summary>
    Nothing,

    /// <summary>The kilograms carried.</summary>
    Kilograms,

    /// <summary>The kilometres driven.</summary>
    Kilometres,
}
