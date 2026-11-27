namespace Priamus.Trips.Registry.Contracts;

/// <summary>Which locations a trip for a customer on a day must name.</summary>
public enum TripLocationsAsked
{
    /// <summary>None: the locations are optional.</summary>
    None,

    /// <summary>The pickup location.</summary>
    Pickup,

    /// <summary>The delivery location.</summary>
    Delivery,

    /// <summary>Both the pickup and the delivery location.</summary>
    Both,
}
