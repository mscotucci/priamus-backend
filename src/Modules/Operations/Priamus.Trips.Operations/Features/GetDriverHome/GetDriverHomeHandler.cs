using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.GetDriverHome;

/// <summary>
/// Handles <c>GET /api/operations/driver/home</c>: the driver's name, the trip in progress, the planned
/// trips, the trips to repeat and the counts of trips ended today and in the last seven days.
/// </summary>
internal sealed class GetDriverHomeHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetDriverHomeHandler(
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
    /// Reads the driver's name from the Registry, the trip in progress with what its end asks, the planned
    /// trips that have not expired (in the order they were added, with their days to expiry) and, from the
    /// driver's ended trips, one repeatable trip per distinct (customer, pickup, delivery, leading vehicle,
    /// trailer), the one ended last first (the one numbered last when they ended together). It also counts the
    /// driver's ended trips whose day is today and whose day is today or one of the 6 days before.
    /// <see cref="TripErrors.DriverNotFound"/> when the Registry has no driver with the session's identifier.
    /// </summary>
    public async Task<Result<GetDriverHomeResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var driver = await _registry.FindDriverAsync(driverId, cancellationToken);
        if (driver is null)
        {
            return TripErrors.DriverNotFound;
        }

        var today = _clock.TodayInItaly();
        var own = _db.Trips.AsNoTracking().Where(t => t.DriverId == driverId);

        var inProgress = await own
            .Where(t => t.Start != null && t.End == null)
            .FirstOrDefaultAsync(cancellationToken);
        var oldestNotExpired = Trip.OldestAddedOnNotExpired(today);
        var planned = await own
            .Where(t => t.Start == null && t.AddedOn >= oldestNotExpired)
            .OrderBy(t => t.AddedAt)
            .ToListAsync(cancellationToken);

        // The only place that decides when two trips are the same repeatable trip.
        var repeatable = await own
            .Where(t => t.End != null)
            .GroupBy(t => new
            {
                t.CustomerId,
                t.PickupLocationId,
                t.DeliveryLocationId,
                t.LeadingVehicleId,
                t.TrailerId,
            })
            .Select(g => new
            {
                g.Key.CustomerId,
                g.Key.PickupLocationId,
                g.Key.DeliveryLocationId,
                g.Key.LeadingVehicleId,
                g.Key.TrailerId,
                Latest = g.Max(t => t.EndedAt),
                LatestNumber = g.Max(t => t.Number),
            })
            .OrderByDescending(g => g.Latest)
            .ThenByDescending(g => g.LatestNumber)
            .ToListAsync(cancellationToken);

        var endedToday = await own.CountAsync(t => t.End != null && t.Day == today, cancellationToken);
        var weekStart = Trip.FirstDayOfLastSevenDays(today);
        var endedInLastSevenDays = await own.CountAsync(
            t => t.End != null && t.Day >= weekStart && t.Day <= today, cancellationToken);

        var shown = new List<Trip>(planned);
        if (inProgress is not null)
        {
            shown.Add(inProgress);
        }

        var names = await _registry.DescribeAsync(
            [.. shown.SelectMany(t => new[] { t.LeadingVehicleId, t.TrailerId }.OfType<Guid>())
                .Concat(repeatable.SelectMany(r => new[] { r.LeadingVehicleId, r.TrailerId }.OfType<Guid>()))
                .Distinct()],
            [.. shown.Select(t => t.CustomerId).Concat(repeatable.Select(r => r.CustomerId)).Distinct()],
            [.. shown.SelectMany(t => new[] { t.PickupLocationId, t.DeliveryLocationId }.OfType<Guid>())
                .Concat(repeatable.SelectMany(r => new[] { r.PickupLocationId, r.DeliveryLocationId }.OfType<Guid>()))
                .Distinct()],
            cancellationToken);

        var inProgressDto = inProgress is null
            ? null
            : new InProgressTripDto(
                inProgress.ToDto(names),
                await _registry.FindEndAsksAsync(inProgress, cancellationToken));

        var plannedDtos = planned.Select(t => new PlannedTripDto(t.ToDto(names), t.DaysToExpiry(today))).ToList();

        var trips = repeatable.Select(r => new RepeatableTripDto(
            names.CustomerOf(r.CustomerId),
            r.PickupLocationId is { } pickupId ? names.LocationOf(pickupId) : null,
            r.DeliveryLocationId is { } deliveryId ? names.LocationOf(deliveryId) : null,
            names.VehicleOf(r.LeadingVehicleId),
            r.TrailerId is { } trailerId ? names.VehicleOf(trailerId) : null)).ToList();

        return new GetDriverHomeResponse(driver.FirstName, driver.LastName, inProgressDto, plannedDtos, trips, endedToday, endedInLastSevenDays);
    }
}
