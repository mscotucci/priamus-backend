using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.ListMyTrips;

/// <summary>
/// Handles <c>GET /api/operations/driver/my-trips</c>: the driver's own ended trips of the last seven days.
/// </summary>
internal sealed class ListMyTripsHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public ListMyTripsHandler(
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
    /// Reads the session driver's ended trips whose day is today or one of the 6 days before (Italian day),
    /// the newest first by day then by start time (the one numbered last when both match), named by one
    /// registry read.
    /// </summary>
    public async Task<Result<MyTripsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        // The Driver policy requires the claim, so a handler reached without it is a wiring bug.
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var today = _clock.TodayInItaly();
        var from = Trip.FirstDayOfLastSevenDays(today);

        var trips = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driverId && t.End != null && t.Day >= from && t.Day <= today)
            .OrderByDescending(t => t.Day)
            .ThenByDescending(t => t.Start)
            .ThenByDescending(t => t.Number)
            .ToListAsync(cancellationToken);

        var names = await _registry.DescribeAsync(
            [.. trips.SelectMany(t => new[] { t.LeadingVehicleId, t.TrailerId }.OfType<Guid>()).Distinct()],
            [.. trips.Select(t => t.CustomerId).Distinct()],
            [.. trips.SelectMany(t => new[] { t.PickupLocationId, t.DeliveryLocationId }.OfType<Guid>()).Distinct()],
            cancellationToken);

        return new MyTripsResponse(today, [.. trips.Select(t => t.ToDto(names))]);
    }
}
