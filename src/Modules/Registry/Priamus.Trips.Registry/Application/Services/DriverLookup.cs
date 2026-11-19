using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Resolves the driver linked to a user account for the session claims (ADR 0008, ADR 0010). Only an
/// active driver counts: a deactivated one answers <c>null</c>, so the next refresh drops the claim.
/// </summary>
internal sealed class DriverLookup : IDriverLookup
{
    private readonly RegistryDbContext _db;

    public DriverLookup(RegistryDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Guid?> GetDriverIdByUserIdAsync(UserId userId, CancellationToken cancellationToken)
    {
        var id = await _db.Drivers
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.Status == DriverStatus.Active)
            .Select(d => (DriverId?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id?.Value;
    }
}
