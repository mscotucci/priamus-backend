using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Operations.Features.ListTripLocations;

/// <summary>
/// Handles <c>GET /api/operations/driver/locations</c>: the active locations the driver may choose
/// from, in the order the Registry gives them (name, then municipality).
/// </summary>
internal sealed class ListTripLocationsHandler
{
    private readonly ITripRegistry _registry;

    public ListTripLocationsHandler(ITripRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Asks the Registry for the active locations matching the trimmed search text.
    /// </summary>
    public async Task<Result<IReadOnlyList<TripLocationDto>>> HandleAsync(
        ListTripLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var locations = await _registry.ListLocationsAsync(request.Search?.Trim(), cancellationToken);

        return locations
            .Select(l => new TripLocationDto(l.Id, l.Name, l.Municipality))
            .ToList();
    }
}
