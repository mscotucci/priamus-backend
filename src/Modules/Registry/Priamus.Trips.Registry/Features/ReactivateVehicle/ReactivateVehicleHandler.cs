using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.ReactivateVehicle;

/// <summary>
/// Handles <c>POST /api/registry/vehicles/{id}/reactivate</c>: puts a decommissioned vehicle back in service (PRD RF-26, RF-29).
/// </summary>
/// <remarks>
/// The version the client read becomes the original value of the concurrency token, so a vehicle
/// changed in the meantime fails the save with <see cref="DbUpdateConcurrencyException"/>, answered
/// as <see cref="VehicleErrors.ConcurrentUpdate"/>. A stale version is refused even when the vehicle
/// already has the requested status; only a matching version reaches the aggregate's no-op, which
/// answers 204 without an audit row.
/// </remarks>
internal sealed class ReactivateVehicleHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReactivateVehicleHandler> _logger;

    public ReactivateVehicleHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<ReactivateVehicleHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Reactivates the vehicle identified by <paramref name="id"/>.
    /// </summary>
    public async Task<Result> HandleAsync(
        Guid id,
        ReactivateVehicleRequest request,
        CancellationToken cancellationToken)
    {
        var vehicleId = new VehicleId(id);
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return VehicleErrors.NotFound;
        }

        var actorId = _currentUser.UserId;

        vehicle.Reactivate(_clock, actorId);

        _db.Entry(vehicle).Property(v => v.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VehicleErrors.ConcurrentUpdate;
        }

        _logger.LogInformation(
            "Vehicle {VehicleId} reactivated by actor {ActorId}", vehicle.Id.Value, actorId.Value);

        return Result.Success();
    }
}
