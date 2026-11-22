using Priamus.Trips.Registry.Domain.Locations;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// A location a rate of a contract is for, as the API returns it.
/// </summary>
/// <param name="Id">The location's identifier.</param>
/// <param name="Name">The location's name.</param>
/// <param name="Municipality">The location's municipality.</param>
/// <param name="Status">The location's status: a deactivated location keeps its rate.</param>
public sealed record RatedLocationDto(Guid Id, string Name, string Municipality, LocationStatus Status);
