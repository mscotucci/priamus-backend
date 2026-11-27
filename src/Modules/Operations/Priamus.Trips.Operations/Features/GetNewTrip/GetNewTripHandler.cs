using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.GetNewTrip;

/// <summary>
/// Handles <c>GET /api/operations/driver/new-trip</c>: the vehicle and trailer of the driver's most
/// recent trip started today, and whether the driver has a trip in progress (RF-08bis).
/// </summary>
internal sealed class GetNewTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetNewTripHandler(
        OperationsDbContext db,
        ITripRegistry registry,
        IClock clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Finds the driver's most recent trip started today (by day and start time); its vehicle is proposed
    /// when the Registry says it still holds, its trailer when the trailer also holds.
    /// </summary>
    public async Task<Result<GetNewTripResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var today = _clock.TodayInItaly();
        var hasTripInProgress = await _db.Trips.AsNoTracking()
            .AnyAsync(t => t.DriverId == driverId && t.Start != null && t.End == null, cancellationToken);
        var latest = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && t.Day == today)
            .OrderByDescending(t => t.Start)
            .Select(t => new { t.LeadingVehicleId, t.TrailerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return new GetNewTripResponse(null, null, hasTripInProgress);
        }

        var check = await _registry.CheckAnswersAsync(
            new TripAnswers(latest.LeadingVehicleId, latest.TrailerId, null, today, null, null),
            cancellationToken);

        Guid? vehicleId = check.Vehicle ? latest.LeadingVehicleId : null;
        Guid? trailerId = check.Vehicle && check.Trailer ? latest.TrailerId : null;
        var names = await _registry.DescribeAsync(
            new[] { vehicleId, trailerId }.OfType<Guid>().ToList(), [], [], cancellationToken);

        return new GetNewTripResponse(
            vehicleId is { } vehicle ? names.VehicleOf(vehicle) : null,
            trailerId is { } trailer ? names.VehicleOf(trailer) : null,
            hasTripInProgress);
    }
}
