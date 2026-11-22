using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.ListRateableLocations;

/// <summary>
/// Handles <c>GET /api/registry/locations/rateable</c>: every active location, ordered by name then
/// municipality, not paged.
/// </summary>
internal sealed class ListRateableLocationsHandler
{
    private readonly RegistryDbContext _db;

    public ListRateableLocationsHandler(RegistryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lists the locations a location rate can be given to.
    /// </summary>
    public async Task<Result<IReadOnlyList<RateableLocationDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var locations = await _db.Locations
            .AsNoTracking()
            .Where(l => l.Status == LocationStatus.Active)
            .ToListAsync(cancellationToken);

        IReadOnlyList<RateableLocationDto> result = locations
            .OrderBy(l => l.Name, StringComparer.Ordinal)
            .ThenBy(l => l.Municipality, StringComparer.Ordinal)
            .Select(l => new RateableLocationDto(l.Id.Value, l.Name, l.Municipality))
            .ToList();

        return Result<IReadOnlyList<RateableLocationDto>>.Success(result);
    }
}
