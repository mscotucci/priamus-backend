using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// A location rate of a contract as the API returns it, with the locations it is for. A side the
/// billing level does not use is <c>null</c>.
/// </summary>
/// <param name="Pickup">The pickup location; <c>null</c> when the billing level does not use it.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when the billing level does not use it.</param>
/// <param name="DayPrice">The day price.</param>
/// <param name="NightPrice">The night price; <c>null</c> without the night surcharge.</param>
/// <param name="NightCallFee">The night call fee; <c>null</c> without the night surcharge.</param>
/// <param name="MinimumRule">The kind of minimum rule.</param>
/// <param name="MinimumAmount">The minimum amount M; <c>null</c> unless the rule has one.</param>
/// <param name="MinimumThreshold">The threshold X; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="MinimumOveragePrice">The overage price Y; <c>null</c> unless the rule is tiered overage.</param>
public sealed record ContractLocationRateDto(
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
    /// Builds the location rates of every contract in one query on the locations, ordered by pickup name,
    /// pickup municipality, delivery name, then delivery municipality.
    /// </summary>
    /// <param name="db">The Registry database.</param>
    /// <param name="contracts">The contracts to build the location rates of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The location rates by contract; every contract is present, with an empty list when it has none.</returns>
    internal static async Task<IReadOnlyDictionary<ContractId, IReadOnlyList<ContractLocationRateDto>>> LoadAsync(
        RegistryDbContext db,
        IReadOnlyCollection<Contract> contracts,
        CancellationToken cancellationToken)
    {
        var locationIds = contracts
            .SelectMany(c => c.LocationRates)
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
            c => (IReadOnlyList<ContractLocationRateDto>)[.. c.LocationRates
                .Select(r => From(r, locations))
                .OrderBy(d => d.Pickup?.Name, StringComparer.Ordinal)
                .ThenBy(d => d.Pickup?.Municipality, StringComparer.Ordinal)
                .ThenBy(d => d.Delivery?.Name, StringComparer.Ordinal)
                .ThenBy(d => d.Delivery?.Municipality, StringComparer.Ordinal)]);
    }

    private static ContractLocationRateDto From(LocationRate locationRate, Dictionary<LocationId, Location> locations)
    {
        var pickup = locationRate.PickupLocationId is { } pickupId ? locations[pickupId] : null;
        var delivery = locationRate.DeliveryLocationId is { } deliveryId ? locations[deliveryId] : null;

        return new(
            pickup is null ? null : RatedLocation(pickup),
            delivery is null ? null : RatedLocation(delivery),
            locationRate.Rate.DayPrice,
            locationRate.Rate.NightPrice,
            locationRate.Rate.NightCallFee,
            locationRate.Rate.Minimum.Kind,
            locationRate.Rate.Minimum.Amount,
            locationRate.Rate.Minimum.Threshold,
            locationRate.Rate.Minimum.OveragePrice);
    }

    private static RatedLocationDto RatedLocation(Location location) =>
        new(location.Id.Value, location.Name, location.Municipality, location.Status);
}
