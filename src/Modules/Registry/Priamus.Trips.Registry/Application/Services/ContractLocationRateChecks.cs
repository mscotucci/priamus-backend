using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// The checks on the locations of a contract's location rates, which need the locations and so are
/// not invariants of the contract.
/// </summary>
internal static class ContractLocationRateChecks
{
    /// <summary>
    /// Looks, in the order of the list and the pickup before the delivery, for the first location
    /// that cannot have a rate: one that does not exist, or a deactivated one that no rate of the
    /// stored contract names on either side. When found, stores the id (the name and municipality
    /// for a deactivated one) in <paramref name="arguments"/> for the Italian message and returns the
    /// error; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="locationRates">The location rates being written.</param>
    /// <param name="stored">The location rates the stored contract already has; empty on create.</param>
    /// <param name="arguments">Receives the id or the name and municipality for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        IReadOnlyList<LocationRate> locationRates,
        IReadOnlyList<LocationRate> stored,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (locationRates.Count == 0)
        {
            return null;
        }

        var ids = locationRates
            .SelectMany(r => new[] { r.PickupLocationId, r.DeliveryLocationId })
            .OfType<LocationId>()
            .ToList();
        var locations = await db.Locations
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        var storedIds = stored
            .SelectMany(r => new[] { r.PickupLocationId, r.DeliveryLocationId })
            .OfType<LocationId>()
            .ToHashSet();

        foreach (var id in ids)
        {
            if (!locations.TryGetValue(id, out var location))
            {
                arguments.Set(id.Value);
                return ContractErrors.LocationRateUnknownLocation;
            }

            if (location.Status is LocationStatus.Deactivated && !storedIds.Contains(id))
            {
                arguments.Set($"{location.Name}, {location.Municipality}");
                return ContractErrors.LocationRateDeactivatedLocation;
            }
        }

        return null;
    }
}
