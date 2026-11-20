using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListRateableVehicles;

/// <summary>
/// Handles <c>GET /api/registry/vehicles/rateable</c>: every active tractor and standalone vehicle,
/// ordered by plate, not paged.
/// </summary>
internal sealed class ListRateableVehiclesHandler
{
    private readonly RegistryDbContext _db;

    public ListRateableVehiclesHandler(RegistryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lists the vehicles a vehicle rate can be given to.
    /// </summary>
    public async Task<Result<IReadOnlyList<RateableVehicleDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Where(v => v.Status == VehicleStatus.Active
                && (v.Type == VehicleType.Tractor || v.Type == VehicleType.StandaloneVehicle))
            .ToListAsync(cancellationToken);

        IReadOnlyList<RateableVehicleDto> result = vehicles
            .OrderBy(v => v.LicensePlate.Value, StringComparer.Ordinal)
            .Select(v => new RateableVehicleDto(v.Id.Value, v.LicensePlate.Value, v.Description))
            .ToList();

        return Result<IReadOnlyList<RateableVehicleDto>>.Success(result);
    }
}
