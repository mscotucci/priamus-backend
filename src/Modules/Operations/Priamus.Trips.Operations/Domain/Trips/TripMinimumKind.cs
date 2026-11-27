namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>The kind of minimum rule of the rate a trip was priced with.</summary>
public enum TripMinimumKind
{
    /// <summary>No minimum.</summary>
    None,

    /// <summary>A fixed minimum amount.</summary>
    FixedMinimum,

    /// <summary>An amount up to a threshold, a price per unit beyond it.</summary>
    TieredOverage,
}
