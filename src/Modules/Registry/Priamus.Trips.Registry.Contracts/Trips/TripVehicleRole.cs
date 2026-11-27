namespace Priamus.Trips.Registry.Contracts;

/// <summary>The part a vehicle plays in a trip, which says which vehicles can be offered.</summary>
public enum TripVehicleRole
{
    /// <summary>The vehicle that leads the trip: an active tractor or standalone vehicle.</summary>
    Leading,

    /// <summary>The trailer behind a tractor: an active trailer.</summary>
    Trailer,
}
