namespace Priamus.Trips.Registry.Features.ListRateableLocations;

/// <summary>
/// A location that can be given a location rate in a contract.
/// </summary>
/// <param name="Id">The location's identifier.</param>
/// <param name="Name">What people call the place.</param>
/// <param name="Municipality">The town the location is in.</param>
public sealed record RateableLocationDto(Guid Id, string Name, string Municipality);
