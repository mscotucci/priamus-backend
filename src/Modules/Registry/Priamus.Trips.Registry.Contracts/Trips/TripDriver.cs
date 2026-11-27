namespace Priamus.Trips.Registry.Contracts;

/// <summary>The name of a driver, as a trip shows it.</summary>
/// <param name="FirstName">The first name of the driver.</param>
/// <param name="LastName">The last name of the driver.</param>
public sealed record TripDriver(string FirstName, string LastName);
