using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// The checks on the vehicles of a contract's vehicle rates, which need the vehicles and so are not
/// invariants of the contract.
/// </summary>
internal static class ContractVehicleRateChecks
{
    /// <summary>
    /// Looks, in the order of the list, for the first vehicle that cannot have a vehicle rate: one that
    /// does not exist, a trailer, or a decommissioned one the contract does not already have. When
    /// found, stores the plate (the id for an unknown vehicle) in <paramref name="arguments"/> for
    /// the Italian message and returns the error; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="vehicleRates">The vehicle rates being written.</param>
    /// <param name="stored">The vehicle rates the stored contract already has; empty on create.</param>
    /// <param name="arguments">Receives the plate or the id for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<VehicleRate> stored,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (vehicleRates.Count == 0)
        {
            return null;
        }

        var ids = vehicleRates.Select(r => r.VehicleId).ToList();
        var vehicles = await db.Vehicles
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        foreach (var id in ids)
        {
            if (!vehicles.TryGetValue(id, out var vehicle))
            {
                arguments.Set(id.Value);
                return ContractErrors.VehicleRateUnknownVehicle;
            }

            if (vehicle.Type is VehicleType.Trailer)
            {
                arguments.Set(vehicle.LicensePlate.Value);
                return ContractErrors.VehicleRateOnTrailer;
            }

            if (vehicle.Status is VehicleStatus.Decommissioned && stored.All(r => r.VehicleId != id))
            {
                arguments.Set(vehicle.LicensePlate.Value);
                return ContractErrors.VehicleRateDecommissioned;
            }
        }

        return null;
    }
}
