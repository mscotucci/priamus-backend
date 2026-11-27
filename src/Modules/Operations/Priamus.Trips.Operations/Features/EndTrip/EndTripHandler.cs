using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.EndTrip;

/// <summary>
/// Handles <c>POST /api/operations/driver/trips/{tripId}/end</c>: ends the trip in progress now or at a
/// time in the past, prices it with the registry and gives it its number.
/// </summary>
internal sealed class EndTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ErrorMessageArguments _messageArguments;

    public EndTripHandler(
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
    /// is <see cref="TripErrors.OwnedByAnotherDriver"/>. A trip already ended is answered as it is; a
    /// planned one is <see cref="TripErrors.NotInProgress"/>. Otherwise, in this order: a typed time after now is
    /// <see cref="TripErrors.InTheFuture"/>, a trip that would last less than 1 minute or 1440 or more is
    /// <see cref="TripErrors.DurationOutOfRange"/>, an end that closes another trip's start is
    /// <see cref="TripErrors.Overlaps"/>, then the registry prices the trip (its errors pass through).
    /// A save lost to a competing act on the same trip (its version changed) is answered by reading the trip
    /// again, once, through the paths above: a trip ended meanwhile is answered as it is, a trip deleted
    /// meanwhile is <see cref="TripErrors.NotFound"/>.
    /// </summary>
    public Task<Result<TripDto>> HandleAsync(
        Guid tripId, EndTripRequest request, CancellationToken cancellationToken) =>
        HandleAsync(tripId, request, isRetry: false, cancellationToken);

    private async Task<Result<TripDto>> HandleAsync(
        Guid tripId, EndTripRequest request, bool isRetry, CancellationToken cancellationToken)
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
        if (trip.IsExpiredOn(DateOnly.FromDateTime(nowToTheMinute)))
        {
            return TripErrors.NotFound;
        }

        if (trip.State == TripState.Ended)
        {
            return await trip.ToDtoAsync(_registry, cancellationToken);
        }

        if (trip.State != TripState.InProgress)
        {
            return TripErrors.NotInProgress;
        }

        var day = trip.Day!.Value;
        var start = trip.Start!.Value;
        var startsAt = day.ToDateTime(start);

        TimeOnly time;
        DateTime endsAt;
        if (request.Time is { } typed)
        {
            // A typed time is cut to the minute like the time now: a trip is stored by the minute.
            time = new TimeOnly(typed.Hour, typed.Minute);
            endsAt = TripSchedule.Of(day, start, time).EndsAt!.Value;
            if (TripSchedule.IsInTheFuture(endsAt, nowToTheMinute))
            {
                return TripErrors.InTheFuture;
            }
        }
        else
        {
            time = TimeOnly.FromDateTime(nowToTheMinute);
            endsAt = nowToTheMinute;
        }

        if ((endsAt - startsAt).TotalMinutes is < 1 or > Trip.MaximumMinutes)
        {
            return TripErrors.DurationOutOfRange;
        }

        // The other trips of the driver are all ended: only one trip is in progress at a time.
        var others = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && t.Id != id && t.Start != null && t.End != null)
            .Select(t => new { t.Day, t.Start, t.End })
            .ToListAsync(cancellationToken);
        var overlap = TripSchedule.FindOverlapOfEnd(
            startsAt,
            endsAt,
            others.Select(o => TripSchedule.Of(o.Day!.Value, o.Start!.Value, o.End)));
        if (overlap is { } other)
        {
            _messageArguments.Set(TripErrors.OverlapArguments(other.StartsAt, other.EndsAt!.Value));
            return TripErrors.Overlaps;
        }

        var priced = await _registry.PriceTripAsync(
            new TripToPrice(
                trip.CustomerId, day, trip.LeadingVehicleId, trip.TrailerId,
                trip.PickupLocationId, trip.DeliveryLocationId, start, time,
                request.Kilograms, request.Kilometres),
            cancellationToken);
        if (priced.IsFailure)
        {
            return priced.Error;
        }

        // A number taken here is burnt when the save below loses to a competing act: trip numbers only grow,
        // they are not gapless.
        var number = await _db.NextTripNumberAsync(cancellationToken);
        var ended = trip.EndAt(time, request.Kilograms, request.Kilometres, priced.Value, number, _clock.UtcNow);
        if (ended.IsFailure)
        {
            return ended.Error;
        }

        if (!await _db.TrySaveTripAsync(isRetry, cancellationToken))
        {
            return await HandleAsync(tripId, request, isRetry: true, cancellationToken);
        }

        return await trip.ToDtoAsync(_registry, cancellationToken);
    }
}
