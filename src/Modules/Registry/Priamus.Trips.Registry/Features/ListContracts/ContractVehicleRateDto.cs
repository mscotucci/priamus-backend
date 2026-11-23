using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// A vehicle rate of a contract as the API returns it, with the vehicle and the locations it is for. A
/// side the billing level does not use is <c>null</c>.
/// </summary>
/// <param name="VehicleId">The vehicle the rate is for.</param>
/// <param name="LicensePlate">The vehicle's license plate.</param>
/// <param name="Description">The vehicle's description.</param>
/// <param name="VehicleStatus">The vehicle's status: a decommissioned vehicle keeps its vehicle rate.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when the billing level does not use it.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when the billing level does not use it.</param>
/// <param name="DayPrice">The day price.</param>
/// <param name="NightPrice">The night price; <c>null</c> without the night surcharge.</param>
/// <param name="NightCallFee">The night call fee; <c>null</c> without the night surcharge.</param>
/// <param name="MinimumRule">The kind of minimum rule.</param>
/// <param name="MinimumAmount">The minimum amount M; <c>null</c> unless the rule has one.</param>
/// <param name="MinimumThreshold">The threshold X; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="MinimumOveragePrice">The overage price Y; <c>null</c> unless the rule is tiered overage.</param>
public sealed record ContractVehicleRateDto(
    Guid VehicleId,
    string LicensePlate,
    string Description,
    VehicleStatus VehicleStatus,
    RatedLocationDto? Pickup,
    RatedLocationDto? Delivery,
    decimal DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    MinimumRuleKind MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice)
{
    /// <summary>
    /// Builds the vehicle rates of every contract in one query on the vehicles and one on the locations,
    /// ordered by license plate, then pickup name, pickup municipality, delivery name, then delivery
    /// municipality.
    /// </summary>
    /// <param name="db">The Registry database.</param>
    /// <param name="contracts">The contracts to build the vehicle rates of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vehicle rates by contract; every contract is present, with an empty list when it has none.</returns>
    internal static async Task<IReadOnlyDictionary<ContractId, IReadOnlyList<ContractVehicleRateDto>>> LoadAsync(
        RegistryDbContext db,
        IReadOnlyCollection<Contract> contracts,
        CancellationToken cancellationToken)
    {
        var vehicleIds = contracts
            .SelectMany(c => c.VehicleRates)
            .Select(r => r.VehicleId)
            .Distinct()
            .ToList();

        var vehicles = vehicleIds.Count == 0
            ? []
            : await db.Vehicles
                .AsNoTracking()
                .Where(v => vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, cancellationToken);

        var locationIds = contracts
            .SelectMany(c => c.VehicleRates)
            .SelectMany(r => new[] { r.PickupLocationId, r.DeliveryLocationId })
            .OfType<LocationId>()
            .Distinct()
            .ToList();

        var locations = locationIds.Count == 0
            ? []
            : await db.Locations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);

        return contracts.ToDictionary(
            c => c.Id,
            c => (IReadOnlyList<ContractVehicleRateDto>)[.. c.VehicleRates
                .Select(r => From(r, vehicles[r.VehicleId], locations))
                .OrderBy(d => d.LicensePlate, StringComparer.Ordinal)
                .ThenBy(d => d.Pickup?.Name, StringComparer.Ordinal)
                .ThenBy(d => d.Pickup?.Municipality, StringComparer.Ordinal)
                .ThenBy(d => d.Delivery?.Name, StringComparer.Ordinal)
                .ThenBy(d => d.Delivery?.Municipality, StringComparer.Ordinal)]);
    }

    private static ContractVehicleRateDto From(VehicleRate vehicleRate, Vehicle vehicle, Dictionary<LocationId, Location> locations)
    {
        var pickup = vehicleRate.PickupLocationId is { } pickupId ? locations[pickupId] : null;
        var delivery = vehicleRate.DeliveryLocationId is { } deliveryId ? locations[deliveryId] : null;

        return new(
            vehicle.Id.Value,
            vehicle.LicensePlate.Value,
            vehicle.Description,
            vehicle.Status,
            pickup is null ? null : ContractLocationRateDto.RatedLocation(pickup),
            delivery is null ? null : ContractLocationRateDto.RatedLocation(delivery),
            vehicleRate.Rate.DayPrice,
            vehicleRate.Rate.NightPrice,
            vehicleRate.Rate.NightCallFee,
            vehicleRate.Rate.Minimum.Kind,
            vehicleRate.Rate.Minimum.Amount,
            vehicleRate.Rate.Minimum.Threshold,
            vehicleRate.Rate.Minimum.OveragePrice);
    }
}
