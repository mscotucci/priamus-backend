using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Operations.Features.ListTripVehicles;

/// <summary>
/// Handles <c>GET /api/operations/driver/vehicles</c>: the active vehicles of the asked role, in the
/// order the Registry gives them (by plate).
/// </summary>
internal sealed class ListTripVehiclesHandler
{
    private readonly ITripRegistry _registry;

    public ListTripVehiclesHandler(ITripRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Asks the Registry for the active vehicles of the role matching the trimmed search text.
    /// </summary>
    public async Task<Result<IReadOnlyList<TripVehicleDto>>> HandleAsync(
        ListTripVehiclesRequest request,
        CancellationToken cancellationToken)
    {
        // The request validation guarantees the role.
        var vehicles = await _registry.ListVehiclesAsync(
            request.Role!.Value, request.Search?.Trim(), cancellationToken);

        return vehicles
            .Select(v => new TripVehicleDto(v.Id, v.LicensePlate, v.Description, v.Kind, v.RequiresTrailer))
            .ToList();
    }
}
