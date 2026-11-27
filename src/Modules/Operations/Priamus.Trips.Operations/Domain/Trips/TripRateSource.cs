namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>Where the rate a trip was priced with came from.</summary>
public enum TripRateSource
{
    /// <summary>The rate of the contract.</summary>
    Contract,

    /// <summary>The rate a vehicle has of its own in the contract.</summary>
    Vehicle,

    /// <summary>The rate of the pickup location or of the delivery location.</summary>
    Location,

    /// <summary>The rate of the pair of pickup and delivery location.</summary>
    LocationPair,
}
