using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.DeactivateDriver;

/// <summary>
/// Handles <c>POST /api/registry/drivers/{id}/deactivate</c>: deactivates a driver (PRD RF-25).
/// </summary>
/// <remarks>
/// The version the client read is compared with the loaded one first, so a stale client always gets
/// <see cref="DriverErrors.ConcurrentUpdate"/>, even when the driver already has the requested
/// status; only a matching version reaches the aggregate's no-op, which answers 204 without an audit
/// row. The version also becomes the original value of the concurrency token, so a driver changed
/// between the load and the save fails with <see cref="DbUpdateConcurrencyException"/>, answered the
/// same way.
/// </remarks>
internal sealed class DeactivateDriverHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DeactivateDriverHandler> _logger;

    public DeactivateDriverHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<DeactivateDriverHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Deactivates the driver identified by <paramref name="id"/>.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        DeactivateDriverRequest request,
        CancellationToken cancellationToken)
    {
        var driverId = new DriverId(id);
        var driver = await _db.Drivers.FirstOrDefaultAsync(c => c.Id == driverId, cancellationToken);
        if (driver is null)
        {
            return DriverErrors.NotFound;
        }

        if (driver.Version != request.Version!.Value)
        {
            return DriverErrors.ConcurrentUpdate;
        }

        var actorId = _currentUser.UserId;

        driver.Deactivate(_clock, actorId);

        _db.Entry(driver).Property(c => c.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DriverErrors.ConcurrentUpdate;
        }

        _logger.LogInformation(
            "Driver {DriverId} deactivated by actor {ActorId}", driver.Id.Value, actorId.Value);

        return Result.Success();
    }
}
