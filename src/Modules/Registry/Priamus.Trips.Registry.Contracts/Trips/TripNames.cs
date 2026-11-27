namespace Priamus.Trips.Registry.Contracts;

/// <summary>The vehicles, customers and locations of past trips, found by identifier whatever their status.</summary>
/// <param name="Vehicles">The vehicles found.</param>
/// <param name="Customers">The customers found.</param>
/// <param name="Locations">The locations found.</param>
public sealed record TripNames(
    IReadOnlyList<TripVehicle> Vehicles,
    IReadOnlyList<TripCustomer> Customers,
    IReadOnlyList<TripLocation> Locations);
