using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.GetNightBand;

/// <summary>
/// Handles <c>GET /api/registry/night-band</c>: returns the system night band.
/// </summary>
internal sealed class GetNightBandHandler
{
    private readonly RegistryDbContext _db;

    public GetNightBandHandler(RegistryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads the one night band row, seeded by the migration.
    /// </summary>
    public async Task<Result<NightBandDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var setting = await _db.NightBandSettings.AsNoTracking().SingleAsync(cancellationToken);
        return NightBandDto.From(setting);
    }
}
