namespace Priamus.Trips.Registry.Features.ListRateableVehicles;

/// <summary>
/// A vehicle that can be given a vehicle rate in a contract.
/// </summary>
/// <param name="Id">The vehicle's identifier.</param>
/// <param name="LicensePlate">The plate in normal form.</param>
/// <param name="Description">The description.</param>
public sealed record RateableVehicleDto(Guid Id, string LicensePlate, string Description);
