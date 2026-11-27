using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.StartTrip;

/// <summary>
/// Handles <c>POST /api/operations/driver/trips/{tripId}/start</c>: starts a planned trip now or at a day
/// and time in the past.
/// </summary>
internal sealed class StartTripHandler
{
    private const string InProgressIndexName = "uq_trips_driver_id_in_progress";

    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _messageArguments;

    public StartTripHandler(
        OperationsDbContext db,
        ITripRegistry registry,
        IClock clock,
        ICurrentUser currentUser,
        ErrorMessageArguments messageArguments)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
        _currentUser = currentUser;
        _messageArguments = messageArguments;
    }

    /// <summary>
    /// A trip that is unknown, deleted or expired is <see cref="TripErrors.NotFound"/>, and another driver's
    /// is <see cref="TripErrors.OwnedByAnotherDriver"/>. A trip already started or ended is answered as it
    /// is. Otherwise, in this order: <see cref="TripErrors.AnotherInProgress"/> while the driver has a trip
    /// in progress, <see cref="TripErrors.InTheFuture"/> for a typed moment after now, the registry's
    /// judgement of the answers on the trip day, and <see cref="TripErrors.Overlaps"/> with the driver's
    /// other trips. A save lost to the in-progress index answers like <see cref="TripErrors.AnotherInProgress"/>.
    /// A save lost to a competing act on the same trip (its version changed) is answered by reading the trip
    /// again, once, through the paths above: a trip started meanwhile is answered as it is, a trip deleted
    /// meanwhile is <see cref="TripErrors.NotFound"/>.
    /// </summary>
    public Task<Result<TripDto>> HandleAsync(
        Guid tripId, StartTripRequest request, CancellationToken cancellationToken) =>
        HandleAsync(tripId, request, isRetry: false, cancellationToken);

    private async Task<Result<TripDto>> HandleAsync(
        Guid tripId, StartTripRequest request, bool isRetry, CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var id = new TripId(tripId);
        var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return TripErrors.NotFound;
        }

        if (trip.DriverId != driverId)
        {
            return TripErrors.OwnedByAnotherDriver;
        }

        var nowToTheMinute = _clock.NowInItalyToTheMinute();
        var today = DateOnly.FromDateTime(nowToTheMinute);
        if (trip.IsExpiredOn(today))
        {
            return TripErrors.NotFound;
        }

        if (trip.State != TripState.Planned)
        {
            return await trip.ToDtoAsync(_registry, cancellationToken);
        }

        if (await _db.Trips.AsNoTracking()
                .AnyAsync(t => t.DriverId == driverId && t.Start != null && t.End == null, cancellationToken))
        {
            return TripErrors.AnotherInProgress;
        }

        var day = request.Day ?? today;
        // A typed time is cut to the minute like the time now: a trip is stored by the minute.
        var time = request.Time is { } typed
            ? new TimeOnly(typed.Hour, typed.Minute)
            : TimeOnly.FromDateTime(nowToTheMinute);
        var startsAt = day.ToDateTime(time);
        if (TripSchedule.IsInTheFuture(startsAt, nowToTheMinute))
        {
            return TripErrors.InTheFuture;
        }

        var validated = await _registry.ValidateTripAsync(
            new TripAnswers(
                trip.LeadingVehicleId, trip.TrailerId, trip.CustomerId, day,
                trip.PickupLocationId, trip.DeliveryLocationId),
            cancellationToken);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var others = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && t.Id != id && t.Start != null)
            .Select(t => new { t.Day, t.Start, t.End })
            .ToListAsync(cancellationToken);
        var overlap = TripSchedule.FindOverlapOfStart(
            startsAt,
            others.Select(o => TripSchedule.Of(o.Day!.Value, o.Start!.Value, o.End)),
            nowToTheMinute);
        if (overlap is { } other)
        {
            // No trip is in progress here, so the other trip has an end; the fallback only keeps a race
            // with a start saved meanwhile from throwing.
            _messageArguments.Set(TripErrors.OverlapArguments(other.StartsAt, other.EndsAt ?? nowToTheMinute));
            return TripErrors.Overlaps;
        }

        var started = trip.StartAt(day, time);
        if (started.IsFailure)
        {
            return started.Error;
        }

        try
        {
            if (!await _db.TrySaveTripAsync(isRetry, cancellationToken))
            {
                // A competing act changed the trip (another start, a delete): answer from what it is now.
                return await HandleAsync(tripId, request, isRetry: true, cancellationToken);
            }
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                  && pg.ConstraintName == InProgressIndexName)
        {
            return TripErrors.AnotherInProgress;
        }

        return await trip.ToDtoAsync(_registry, cancellationToken);
    }
}
