using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.GetTrip;

/// <summary>
/// Handles <c>GET /api/operations/driver/trips/{tripId}</c>: one trip of the driver with what its page
/// needs.
/// </summary>
internal sealed class GetTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetTripHandler(
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
    /// A trip that is unknown, deleted or expired is <see cref="TripErrors.NotFound"/>, and another driver's
    /// is <see cref="TripErrors.OwnedByAnotherDriver"/>. Otherwise the trip, with its days to expiry and
    /// the proposed start time when it is planned, and with what its end asks when it is in progress.
    /// </summary>
    public async Task<Result<GetTripResponse>> HandleAsync(Guid tripId, CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var id = new TripId(tripId);
        var trip = await _db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return TripErrors.NotFound;
        }

        if (trip.DriverId != driverId)
        {
            return TripErrors.OwnedByAnotherDriver;
        }

        var today = _clock.TodayInItaly();
        if (trip.IsExpiredOn(today))
        {
            return TripErrors.NotFound;
        }

        var dto = await trip.ToDtoAsync(_registry, cancellationToken);
        switch (trip.State)
        {
            case TripState.Planned:
                return new GetTripResponse(
                    dto,
                    trip.DaysToExpiry(today),
                    await FindProposedStartAsync(driverId, today, cancellationToken),
                    null);
            case TripState.InProgress:
                return new GetTripResponse(
                    dto, null, null, await _registry.FindEndAsksAsync(trip, cancellationToken));
            default:
                return new GetTripResponse(dto, null, null, null);
        }
    }

    /// <summary>
    /// The end time of the driver's latest trip that ended today, or <c>null</c> when none did. A trip that
    /// started yesterday and ended after midnight ended today.
    /// </summary>
    private async Task<TimeOnly?> FindProposedStartAsync(
        Guid driverId, DateOnly today, CancellationToken cancellationToken)
    {
        // A trip lasts less than a day, so one that ended today started yesterday or today.
        var yesterday = today.AddDays(-1);
        var candidates = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && t.End != null && t.Day >= yesterday && t.Day <= today)
            .Select(t => new { Day = t.Day!.Value, Start = t.Start!.Value, End = t.End!.Value })
            .ToListAsync(cancellationToken);

        var endedToday = candidates
            .Select(c => new { c.End, EndsAt = TripSchedule.Of(c.Day, c.Start, c.End).EndsAt!.Value })
            .Where(c => DateOnly.FromDateTime(c.EndsAt) == today)
            .OrderByDescending(c => c.EndsAt)
            .FirstOrDefault();

        return endedToday?.End;
    }
}
