namespace Priamus.Trips.Registry.Contracts;

/// <summary>The customers, drivers, vehicles and locations a search text matches, whatever their status.</summary>
/// <param name="CustomerIds">Customers whose company name or VAT number contains the text.</param>
/// <param name="DriverIds">Drivers whose first name, last name or both together contain the text.</param>
/// <param name="VehicleIds">Vehicles whose plate or description contains the text.</param>
/// <param name="LocationIds">Locations whose name or municipality contains the text.</param>
public sealed record TripSearchMatches(
    IReadOnlyList<Guid> CustomerIds,
    IReadOnlyList<Guid> DriverIds,
    IReadOnlyList<Guid> VehicleIds,
    IReadOnlyList<Guid> LocationIds);
