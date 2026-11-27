namespace Priamus.Trips.Registry.Contracts;

/// <summary>The name of a driver with its identifier, as a list of trips shows it.</summary>
/// <param name="Id">The identifier of the driver.</param>
/// <param name="FirstName">The first name of the driver.</param>
/// <param name="LastName">The last name of the driver.</param>
public sealed record TripDriverName(Guid Id, string FirstName, string LastName);
