using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.ReactivateLocation;

/// <summary>
/// Handles <c>POST /api/registry/locations/{id}/reactivate</c>: reactivates a deactivated location.
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one first, so a stale client always gets
/// <see cref="LocationErrors.ConcurrentUpdate"/>, even when the location already has the requested
/// status; only a matching version reaches the aggregate's no-op, which answers 204 without an audit
/// row. The version also becomes the original value of the concurrency token, so a location changed
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the
/// same way.
/// </remarks>
internal sealed class ReactivateLocationHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReactivateLocationHandler> _logger;

    public ReactivateLocationHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<ReactivateLocationHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Reactivates the location identified by <paramref name="id"/>.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        ReactivateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var locationId = new LocationId(id);
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is null)
        {
            return LocationErrors.NotFound;
        }

        if (location.Version != request.Version!.Value)
        {
            return LocationErrors.ConcurrentUpdate;
        }

        var actorId = _currentUser.UserId;

        location.Reactivate(_clock, actorId);

        _db.Entry(location).Property(l => l.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return LocationErrors.ConcurrentUpdate;
        }

        _logger.LogInformation(
            "Location {LocationId} reactivated by actor {ActorId}", location.Id.Value, actorId.Value);

        return Result.Success();
    }
}
