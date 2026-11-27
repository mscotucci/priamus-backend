namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// A choice of places the contract of the customer accepts for a trip: one location, or a pair. A side
/// the contract does not name is <c>null</c>.
/// </summary>
/// <param name="Pickup">The pickup location, when the choice names one.</param>
/// <param name="Delivery">The delivery location, when the choice names one.</param>
public sealed record TripPlace(TripLocation? Pickup, TripLocation? Delivery);
