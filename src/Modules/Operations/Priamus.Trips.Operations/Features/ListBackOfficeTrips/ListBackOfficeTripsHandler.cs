using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Paging;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Operations.Features.ListBackOfficeTrips;

/// <summary>
/// Handles <c>GET /api/operations/backoffice/trips</c>: the trips of one view (ended, in progress or
/// planned), filtered and searched, in a fixed order per view, with the figures of the whole selection for
/// the ended view.
/// </summary>
internal sealed class ListBackOfficeTripsHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;

    public ListBackOfficeTripsHandler(OperationsDbContext db, ITripRegistry registry, IClock clock)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
    }

    /// <summary>
    /// Reads one page of the requested view. A planned trip that has expired and a deleted trip are never
    /// listed nor counted. The search asks the registry once, only for a non-empty text. The ended view
    /// orders by day (default) or total, newest or largest first unless ascending is asked; in progress
    /// trips come newest first and planned trips oldest added first; every order ends on the id.
    /// </summary>
    public async Task<Result<BackOfficeTripListResponse>> HandleAsync(
        ListBackOfficeTripsRequest request,
        CancellationToken cancellationToken)
    {
        var view = request.EffectiveView;
        var text = request.Search?.Trim();
        var matches = string.IsNullOrEmpty(text)
            ? null
            : await _registry.FindMatchesAsync(text, cancellationToken);

        var query = Filter(request, view, text, matches);

        var totalCount = await query.CountAsync(cancellationToken);

        BackOfficeTripFigures? figures = null;
        if (view == TripState.Ended)
        {
            var customers = await query.Select(t => t.CustomerId).Distinct().CountAsync(cancellationToken);
            var amount = await query.SumAsync(t => (decimal?)t.Pricing!.Total, cancellationToken) ?? 0m;
            figures = new BackOfficeTripFigures(totalCount, customers, amount);
        }

        var trips = await Order(query, request, view)
            .Skip(request.Skip)
            .Take(request.EffectivePageSize)
            .ToListAsync(cancellationToken);

        var names = await _registry.DescribeAsync(
            [.. trips.SelectMany(t => new[] { t.LeadingVehicleId, t.TrailerId }.OfType<Guid>()).Distinct()],
            [.. trips.Select(t => t.CustomerId).Distinct()],
            [.. trips.SelectMany(t => new[] { t.PickupLocationId, t.DeliveryLocationId }.OfType<Guid>()).Distinct()],
            cancellationToken);
        var drivers = await _registry.DescribeDriversAsync(
            [.. trips.Select(t => t.DriverId).Distinct()], cancellationToken);

        var today = _clock.TodayInItaly();
        var items = trips.Select(t => ToItem(t, names, drivers, today)).ToList();

        return new BackOfficeTripListResponse(
            new PagedResponse<BackOfficeTripListItemDto>(
                items, totalCount, request.EffectivePage, request.EffectivePageSize),
            figures);
    }

    /// <summary>The selection of the request: the single place that decides which trips a view holds.</summary>
    private IQueryable<Trip> Filter(
        ListBackOfficeTripsRequest request, TripState view, string? text, TripSearchMatches? matches)
    {
        var query = _db.Trips.AsNoTracking();

        if (view == TripState.Planned)
        {
            var oldestNotExpired = Trip.OldestAddedOnNotExpired(_clock.TodayInItaly());
            query = query.Where(t => t.Start == null && t.AddedOn >= oldestNotExpired);
        }
        else if (view == TripState.InProgress)
        {
            query = query.Where(t => t.Start != null && t.End == null);
        }
        else
        {
            query = query.Where(t => t.End != null);
        }

        if (request.From is { } from && request.To is { } to)
        {
            query = query.Where(t => t.Day >= from && t.Day <= to);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(t => t.CustomerId == customerId);
        }

        if (request.DriverId is { } driverId)
        {
            query = query.Where(t => t.DriverId == driverId);
        }

        if (request.VehicleId is { } vehicleId)
        {
            query = query.Where(t => t.LeadingVehicleId == vehicleId || t.TrailerId == vehicleId);
        }

        if (request.LocationId is { } locationId)
        {
            query = query.Where(t => t.PickupLocationId == locationId || t.DeliveryLocationId == locationId);
        }

        if (request.Mode is { } mode)
        {
            query = query.Where(t => t.MeasureKind == mode);
        }

        if (matches is not null)
        {
            // The number matches only a text made of digits; the registry answers for every other field.
            long? number = text!.All(char.IsAsciiDigit) && long.TryParse(text, out var parsed) ? parsed : null;
            var customerIds = matches.CustomerIds.ToArray();
            var driverIds = matches.DriverIds.ToArray();
            var vehicleIds = matches.VehicleIds.ToArray();
            var locationIds = matches.LocationIds.ToArray();
            query = query.Where(t =>
                (number != null && t.Number == number)
                || customerIds.Contains(t.CustomerId)
                || driverIds.Contains(t.DriverId)
                || vehicleIds.Contains(t.LeadingVehicleId)
                || (t.TrailerId != null && vehicleIds.Contains(t.TrailerId.Value))
                || (t.PickupLocationId != null && locationIds.Contains(t.PickupLocationId.Value))
                || (t.DeliveryLocationId != null && locationIds.Contains(t.DeliveryLocationId.Value)));
        }

        return query;
    }

    private static IOrderedQueryable<Trip> Order(
        IQueryable<Trip> query, ListBackOfficeTripsRequest request, TripState view)
    {
        if (view == TripState.Planned)
        {
            return query.OrderBy(t => t.AddedAt).ThenBy(t => t.Id);
        }

        if (view == TripState.InProgress)
        {
            return query.OrderByDescending(t => t.Day).ThenByDescending(t => t.Start).ThenByDescending(t => t.Id);
        }

        // Whitelist: "day" and "total". An unknown column routes to the default (day, newest first) whatever
        // the direction; a known one is descending unless ascending is asked.
        var column = request.SortColumn?.Trim();
        var byTotal = string.Equals(column, "total", StringComparison.OrdinalIgnoreCase);
        var known = byTotal || string.Equals(column, "day", StringComparison.OrdinalIgnoreCase);
        var ascending = known && request.SortDirection == SortDirection.Ascending;

        return (byTotal, ascending) switch
        {
            (true, true) => query.OrderBy(t => t.Pricing!.Total).ThenByDescending(t => t.Number).ThenByDescending(t => t.Id),
            (true, false) => query.OrderByDescending(t => t.Pricing!.Total).ThenByDescending(t => t.Number).ThenByDescending(t => t.Id),
            (false, true) => query.OrderBy(t => t.Day).ThenBy(t => t.Number).ThenBy(t => t.Id),
            (false, false) => query.OrderByDescending(t => t.Day).ThenByDescending(t => t.Number).ThenByDescending(t => t.Id),
        };
    }

    private static BackOfficeTripListItemDto ToItem(
        Trip trip, TripNames names, IReadOnlyList<TripDriverName> drivers, DateOnly today)
    {
        var parts = BackOfficeTripParts.Of(trip, names, drivers);
        var pricing = trip.Pricing;

        return new BackOfficeTripListItemDto(
            trip.Id.Value,
            trip.State,
            trip.Number,
            trip.Day,
            trip.Start,
            trip.End,
            trip.AddedAt,
            trip.State == TripState.Planned ? trip.DaysToExpiry(today) : null,
            parts.Customer,
            parts.Pickup,
            parts.Delivery,
            parts.Driver,
            parts.Vehicle,
            parts.Trailer,
            trip.MeasureKind,
            pricing?.Quantity,
            pricing?.UnitPrice,
            pricing?.Total,
            pricing is null ? null : pricing.IsNight && pricing.NightSurcharge);
    }
}
