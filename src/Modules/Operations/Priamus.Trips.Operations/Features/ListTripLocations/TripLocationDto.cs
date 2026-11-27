namespace Priamus.Trips.Operations.Features.ListTripLocations;

/// <summary>
/// A location the driver can pick up from or deliver to.
/// </summary>
/// <param name="Id">The location's identifier.</param>
/// <param name="Name">The name of the place.</param>
/// <param name="Municipality">The municipality it lies in.</param>
public sealed record TripLocationDto(Guid Id, string Name, string Municipality);
