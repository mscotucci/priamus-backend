using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.AddTrip;

/// <summary>
/// Handles <c>PUT /api/operations/driver/trips/{tripId}</c>: adds a planned trip and, when asked, starts it
/// now.
/// </summary>
internal sealed class AddTripHandler
{
    private const string PrimaryKeyName = "PK_trips";
    private const string InProgressIndexName = "uq_trips_driver_id_in_progress";

    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AddTripHandler(OperationsDbContext db, ITripRegistry registry, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// A trip that already has the identifier is answered as it is when it is the session driver's, is
    /// <see cref="TripErrors.OwnedByAnotherDriver"/> when it is another's, and is <see cref="TripErrors.Deleted"/>
    /// when it was deleted: a deleted trip is not revived. Otherwise the answers are judged
    /// by the registry on today, a start now is refused with <see cref="TripErrors.AnotherInProgress"/> while
    /// the driver has a trip in progress, and the trip is saved. A save lost to a competing save of the same
    /// identifier answers like the existing trip; one lost to the in-progress index answers like
    /// <see cref="TripErrors.AnotherInProgress"/>.
    /// </summary>
    public async Task<Result<AddTripOutcome>> HandleAsync(
        Guid tripId, AddTripRequest request, CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var existing = await FindExistingAsync(tripId, driverId, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var now = _clock.NowInItalyToTheMinute();
        var today = DateOnly.FromDateTime(now);
        var validated = await _registry.ValidateTripAsync(
            new TripAnswers(
                request.VehicleId, request.TrailerId, request.CustomerId, today,
                request.PickupLocationId, request.DeliveryLocationId),
            cancellationToken);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        if (request.Start && await HasTripInProgressAsync(driverId, cancellationToken))
        {
            return TripErrors.AnotherInProgress;
        }

        var trip = Trip.Add(
            new TripId(tripId),
            driverId,
            request.CustomerId!.Value,
            request.VehicleId!.Value,
            request.TrailerId,
            request.PickupLocationId,
            request.DeliveryLocationId,
            _clock.UtcNow,
            today,
            _currentUser.UserId);

        if (request.Start)
        {
            var started = trip.StartAt(today, TimeOnly.FromDateTime(now));
            if (started.IsFailure)
            {
                return started.Error;
            }
        }

        _db.Trips.Add(trip);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                  && pg.ConstraintName == InProgressIndexName)
        {
            return TripErrors.AnotherInProgress;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                  && pg.ConstraintName == PrimaryKeyName)
        {
            // The same identifier was saved meanwhile: answer as if it had been there first.
            _db.Entry(trip).State = EntityState.Detached;
            var saved = await FindExistingAsync(tripId, driverId, cancellationToken);
            if (saved is null)
            {
                throw;
            }

            return saved.Value;
        }

        return new AddTripOutcome(await trip.ToDtoAsync(_registry, cancellationToken), Created: true);
    }

    private async Task<bool> HasTripInProgressAsync(Guid driverId, CancellationToken cancellationToken) =>
        await _db.Trips.AsNoTracking()
            .AnyAsync(t => t.DriverId == driverId && t.Start != null && t.End == null, cancellationToken);

    /// <summary>
    /// The trip with the identifier as the driver's own outcome, the error of another driver's or of a
    /// deleted one, or <c>null</c> when there is none. A deleted trip is looked for with the query filter
    /// ignored, so that it is found.
    /// </summary>
    private async Task<Result<AddTripOutcome>?> FindExistingAsync(
        Guid tripId, Guid driverId, CancellationToken cancellationToken)
    {
        var id = new TripId(tripId);
        var trip = await _db.Trips.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        if (trip.DriverId != driverId)
        {
            return TripErrors.OwnedByAnotherDriver;
        }

        if (trip.DeletedAt is not null)
        {
            return TripErrors.Deleted;
        }

        return new AddTripOutcome(await trip.ToDtoAsync(_registry, cancellationToken), Created: false);
    }
}
