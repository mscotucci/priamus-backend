using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.DeleteTrip;

/// <summary>
/// Handles <c>DELETE /api/operations/driver/trips/{tripId}</c>: marks a planned trip or a trip in progress
/// as deleted. The row is kept.
/// </summary>
internal sealed class DeleteTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public DeleteTripHandler(OperationsDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// An unknown trip or one already deleted succeeds without a change, another driver's is
    /// <see cref="TripErrors.OwnedByAnotherDriver"/>, and an ended trip is
    /// <see cref="TripErrors.EndedTripCannotBeDeleted"/>. Otherwise the trip is marked deleted now by the
    /// current user. A save lost to a competing act on the same trip (its version changed) is answered by
    /// reading the trip again, once, through the paths above: a trip deleted meanwhile succeeds, a trip ended
    /// meanwhile is <see cref="TripErrors.EndedTripCannotBeDeleted"/>.
    /// </summary>
    public Task<Result> HandleAsync(Guid tripId, CancellationToken cancellationToken) =>
        HandleAsync(tripId, isRetry: false, cancellationToken);

    private async Task<Result> HandleAsync(Guid tripId, bool isRetry, CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        // The query filter hides a deleted trip: it is read with the filter ignored so that a repeated
        // deletion finds it and leaves it as it is.
        var id = new TripId(tripId);
        var trip = await _db.Trips.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return Result.Success();
        }

        if (trip.DriverId != driverId)
        {
            return TripErrors.OwnedByAnotherDriver;
        }

        if (trip.DeletedAt is not null)
        {
            return Result.Success();
        }

        var deleted = trip.Delete(_clock.UtcNow, _currentUser.UserId);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        if (!await _db.TrySaveTripAsync(isRetry, cancellationToken))
        {
            return await HandleAsync(tripId, isRetry: true, cancellationToken);
        }

        return Result.Success();
    }
}
