namespace Priamus.Trips.Registry.Contracts;

/// <summary>A place a trip picks up from or delivers to.</summary>
/// <param name="Id">The identifier of the location.</param>
/// <param name="Name">The name of the place.</param>
/// <param name="Municipality">The municipality it lies in.</param>
public sealed record TripLocation(Guid Id, string Name, string Municipality);
